#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <string>
#include <unordered_map>
#include <mutex>
#include <stdexcept>
#include "DefaultPreset.h"

static HMODULE selfModule;
static HMODULE backend;
static std::once_flag backendOnce;
static std::mutex presetMutex;
static std::unordered_map<void*,std::string> presets;
using Word=uintptr_t;
using Generic=Word(*)(Word,Word,Word,Word,Word,Word);
using Setter=int(*)(void*,const char*,unsigned,const char*);

static void Log(const char* message) {
    wchar_t path[32768]; DWORD n=GetEnvironmentVariableW(L"ProgramData",path,32000);
    if(!n || n>=32000)return;
    wcscat(path,L"\\LV1 FaderBank X-Touch Bridge\\surface-binding.log");
    HANDLE f=CreateFileW(path,FILE_APPEND_DATA,FILE_SHARE_READ|FILE_SHARE_WRITE,nullptr,OPEN_ALWAYS,FILE_ATTRIBUTE_NORMAL,nullptr);
    if(f==INVALID_HANDLE_VALUE)return;
    DWORD written;WriteFile(f,message,(DWORD)strlen(message),&written,nullptr);WriteFile(f,"\r\n",2,&written,nullptr);CloseHandle(f);
}

extern "C" void* ResolveBridgeExport(const char* name) {
    std::call_once(backendOnce,[]{
        wchar_t path[32768];DWORD n=GetModuleFileNameW(selfModule,path,32768);
        if(!n || n>=32768)throw std::runtime_error("Bridge module path unavailable");
        wchar_t* slash=wcsrchr(path,L'\\');if(!slash)throw std::runtime_error("Bridge module folder unavailable");
        slash[1]=0;wcscat(path,L"LV1BridgeSupport\\LV1BridgeBackend.dll");
        backend=LoadLibraryExW(path,nullptr,LOAD_WITH_ALTERED_SEARCH_PATH);
        if(!backend){Log("ERROR: backend could not load");throw std::runtime_error("Bridge backend missing");}
    });
    void* address=(void*)GetProcAddress(backend,name);
    if(!address){Log("ERROR: backend export missing");throw std::runtime_error("Bridge export missing");}
    return address;
}

static void SetTag(std::string& block,const char* tag,const char* value) {
    std::string open="<"+std::string(tag)+">",close="</"+std::string(tag)+">";
    auto a=block.find(open);auto b=a==std::string::npos?std::string::npos:block.find(close,a+open.size());
    if(a!=std::string::npos && b!=std::string::npos)block.replace(a+open.size(),b-a-open.size(),value);
    else block+=open+value+close;
}

static std::string Normalize(const char* data,unsigned size) {
    if(!data || !size)return DefaultPreset;
    // Presets are UTF-8 XML fragments, not C++ objects; the host supplies capacity.
    size_t length=0;while(length<size && data[length])length++;
    if(length>1024*1024)throw std::runtime_error("Bridge preset too large");
    std::string xml(data,length);
    auto start=xml.find("<MIDI>"),end=xml.find("</MIDI>");
    if(start==std::string::npos || end==std::string::npos || end<start)throw std::runtime_error("Bridge MIDI preset missing");
    auto content=start+6;std::string block=xml.substr(content,end-content);
    SetTag(block,"NumDevicesConnected","1");SetTag(block,"EnableUserKeys","true");
    const std::string ports="<Surfaces><Surface><Port IN=\"true\" Type=\"0\" Index=\"0\"><Name>FaderBank In</Name><MixerRemoteEnabled>false</MixerRemoteEnabled></Port><Port IN=\"false\" Type=\"0\" Index=\"0\"><Name>FaderBank Out</Name><MixerRemoteEnabled>false</MixerRemoteEnabled></Port></Surface></Surfaces>";
    auto a=block.find("<Surfaces>"),b=block.find("</Surfaces>");
    if(a!=std::string::npos && b!=std::string::npos && b>=a)block.replace(a,b+11-a,ports);
    else {a=block.find("<Surfaces />");if(a!=std::string::npos)block.replace(a,12,ports);else block+=ports;}
    xml.replace(content,end-content,block);return xml;
}

extern "C" Word WMSD_CreateControllerForType(Word a,Word b,Word c,Word d,Word e,Word f) {
    auto real=(Generic)ResolveBridgeExport("WMSD_CreateControllerForType");
    Word result=real(a,b,c,d,e,f);
    if(result){std::lock_guard<std::mutex> lock(presetMutex);presets[(void*)result]=DefaultPreset;}
    Log("Created Bridge controller; automatic binding prepared");return result;
}
extern "C" int WMSD_SetPreset(void* controller,const char* data,unsigned size,const char* version) {
    auto real=(Setter)ResolveBridgeExport("WMSD_SetPreset");
    if(!controller)return real(controller,data,size,version);
    try {
        std::string value=Normalize(data,size);
        int rc=real(controller,value.c_str(),(unsigned)value.size()+1,version);
        if(!rc){std::lock_guard<std::mutex> lock(presetMutex);presets[controller]=value;}
        Log(rc==0?"Loaded preset with automatic MIDI binding":"ERROR: SetPreset returned failure");return rc;
    }catch(const std::exception& error){Log(error.what());return real(controller,data,size,version);}
}
extern "C" Word WMSD_InitControllerForType(Word a,Word b,Word c,Word d,Word e,Word f) {
    if(a){
        std::string value;
        {std::lock_guard<std::mutex> lock(presetMutex);auto it=presets.find((void*)a);value=it==presets.end()?DefaultPreset:it->second;}
        int result=((Setter)ResolveBridgeExport("WMSD_SetPreset"))((void*)a,value.c_str(),(unsigned)value.size()+1,"");
        Log(result==0?"Initialized automatic MIDI binding":"ERROR: automatic initialization rejected");
    }
    return ((Generic)ResolveBridgeExport("WMSD_InitControllerForType"))(a,b,c,d,e,f);
}
extern "C" Word WMSD_DestroyController(Word a,Word b,Word c,Word d,Word e,Word f) {
    {std::lock_guard<std::mutex> lock(presetMutex);presets.erase((void*)a);}
    return ((Generic)ResolveBridgeExport("WMSD_DestroyController"))(a,b,c,d,e,f);
}
extern "C" int WMSD_ShowConfigWindow(void*) {return 0;}
BOOL WINAPI DllMain(HINSTANCE instance,DWORD reason,LPVOID){if(reason==DLL_PROCESS_ATTACH){selfModule=instance;DisableThreadLibraryCalls(instance);}return TRUE;}
