#include <dlfcn.h>
#include <mach-o/dyld.h>
#include <mutex>
#include <string>
#include <unordered_map>
#include <fstream>
#include <cstdlib>

using Word = uintptr_t;
using Generic = Word(*)(Word,Word,Word,Word,Word,Word,Word,Word);
using Setter = int(*)(void*,const char*,unsigned,const char*);

static void* backend = nullptr;
static std::once_flag backendOnce;
static std::mutex presetMutex;
static std::unordered_map<void*, std::string> presets;

static const char* DefaultPreset =
"<MIDI><DisplayMTC>false</DisplayMTC><EnableMuteGroups>false</EnableMuteGroups><EnableUserKeys>true</EnableUserKeys><FollowAuxMasterFlip>false</FollowAuxMasterFlip><LockLayer>0</LockLayer><FollowAuxSendsFlip>true</FollowAuxSendsFlip><EnableJogWheel>false</EnableJogWheel><MasterFaderTrackType>4294967295</MasterFaderTrackType><MasterFaderTrackHandle>0</MasterFaderTrackHandle><NumDevicesConnected>1</NumDevicesConnected><KnobMode>1</KnobMode><FollowDevice>-1</FollowDevice><UserKeyTriangleAction>45</UserKeyTriangleAction><UserKeyTriangleSelectionIndex>4294967295</UserKeyTriangleSelectionIndex><UserKeyTriangleTrackType>4294967295</UserKeyTriangleTrackType><UserKeyTriangleTrackIndex>4294967295</UserKeyTriangleTrackIndex><UserKeySquareAction>46</UserKeySquareAction><UserKeySquareSelectionIndex>4294967295</UserKeySquareSelectionIndex><UserKeySquareTrackType>4294967295</UserKeySquareTrackType><UserKeySquareTrackIndex>4294967295</UserKeySquareTrackIndex><UserKeyCircleAction>0</UserKeyCircleAction><UserKeyCircleSelectionIndex>4294967295</UserKeyCircleSelectionIndex><UserKeyCircleTrackType>4294967295</UserKeyCircleTrackType><UserKeyCircleTrackIndex>4294967295</UserKeyCircleTrackIndex><Surfaces><Surface><Port IN=\"true\" Type=\"0\" Index=\"0\"><Name>FaderBank In</Name><MixerRemoteEnabled>false</MixerRemoteEnabled></Port><Port IN=\"false\" Type=\"0\" Index=\"0\"><Name>FaderBank Out</Name><MixerRemoteEnabled>false</MixerRemoteEnabled></Port></Surface></Surfaces></MIDI>\n"
"<MIDI_D2><DisplayMTC>false</DisplayMTC><EnableMuteGroups>false</EnableMuteGroups><EnableUserKeys>true</EnableUserKeys><FollowAuxMasterFlip>false</FollowAuxMasterFlip><LockLayer>1</LockLayer><FollowAuxSendsFlip>true</FollowAuxSendsFlip><EnableJogWheel>false</EnableJogWheel><MasterFaderTrackType>4294967295</MasterFaderTrackType><MasterFaderTrackHandle>0</MasterFaderTrackHandle><NumDevicesConnected>0</NumDevicesConnected><KnobMode>1</KnobMode><FollowDevice>-1</FollowDevice><UserKeyTriangleAction>45</UserKeyTriangleAction><UserKeyTriangleSelectionIndex>4294967295</UserKeyTriangleSelectionIndex><UserKeyTriangleTrackType>4294967295</UserKeyTriangleTrackType><UserKeyTriangleTrackIndex>4294967295</UserKeyTriangleTrackIndex><UserKeySquareAction>46</UserKeySquareAction><UserKeySquareSelectionIndex>4294967295</UserKeySquareSelectionIndex><UserKeySquareTrackType>4294967295</UserKeySquareTrackType><UserKeySquareTrackIndex>4294967295</UserKeySquareTrackIndex><UserKeyCircleAction>0</UserKeyCircleAction><UserKeyCircleSelectionIndex>4294967295</UserKeyCircleSelectionIndex><UserKeyCircleTrackType>4294967295</UserKeyCircleTrackType><UserKeyCircleTrackIndex>4294967295</UserKeyCircleTrackIndex><Surfaces><Surface /></Surfaces></MIDI_D2>\n";

static void Log(const std::string& message) {
    const char* home = getenv("HOME");
    if (!home) return;
    std::string folder = std::string(home) + "/Library/Logs/LV1Bridge";
    std::string command = "/bin/mkdir -p \"" + folder + "\"";
    system(command.c_str());
    std::ofstream file(folder + "/surface-binding.log", std::ios::app);
    file << message << '\n';
}

static void* Resolve(const char* name) {
    std::call_once(backendOnce, [] {
        Dl_info info{};
        if (!dladdr((void*)&Resolve, &info) || !info.dli_fname) return;
        std::string path(info.dli_fname);
        auto slash = path.find_last_of('/');
        path = path.substr(0, slash + 1) + "LV1BridgeBackend";
        backend = dlopen(path.c_str(), RTLD_NOW | RTLD_LOCAL);
        if (!backend) Log(std::string("Backend load failed: ") + dlerror());
    });
    if (!backend) return nullptr;
    void* symbol = dlsym(backend, name);
    if (!symbol) Log(std::string("Backend export missing: ") + name);
    return symbol;
}

static void SetTag(std::string& block, const char* tag, const char* value) {
    std::string open = "<" + std::string(tag) + ">", close = "</" + std::string(tag) + ">";
    auto a = block.find(open), b = a == std::string::npos ? std::string::npos : block.find(close, a + open.size());
    if (a != std::string::npos && b != std::string::npos) block.replace(a + open.size(), b - a - open.size(), value);
    else block += open + value + close;
}

static std::string Normalize(const char* data, unsigned size) {
    if (!data || !size) return DefaultPreset;
    size_t length = 0; while (length < size && data[length]) ++length;
    std::string xml(data, length);
    auto start = xml.find("<MIDI>"), end = xml.find("</MIDI>");
    if (start == std::string::npos || end == std::string::npos || end < start) return DefaultPreset;
    auto content = start + 6; std::string block = xml.substr(content, end - content);
    SetTag(block, "NumDevicesConnected", "1"); SetTag(block, "EnableUserKeys", "true");
    const std::string ports = "<Surfaces><Surface><Port IN=\"true\" Type=\"0\" Index=\"0\"><Name>FaderBank In</Name><MixerRemoteEnabled>false</MixerRemoteEnabled></Port><Port IN=\"false\" Type=\"0\" Index=\"0\"><Name>FaderBank Out</Name><MixerRemoteEnabled>false</MixerRemoteEnabled></Port></Surface></Surfaces>";
    auto a = block.find("<Surfaces>"), b = block.find("</Surfaces>");
    if (a != std::string::npos && b != std::string::npos) block.replace(a, b + 11 - a, ports);
    else if ((a = block.find("<Surfaces />")) != std::string::npos) block.replace(a, 12, ports);
    else block += ports;
    xml.replace(content, end - content, block);
    return xml;
}

extern "C" __attribute__((visibility("default"))) Word WMSD_CreateControllerForType(Word a,Word b,Word c,Word d,Word e,Word f,Word g,Word h) {
    auto fn = (Generic)Resolve("WMSD_CreateControllerForType"); if (!fn) return 0;
    Word result = fn(a,b,c,d,e,f,g,h);
    if (result) { std::lock_guard<std::mutex> lock(presetMutex); presets[(void*)result] = DefaultPreset; }
    Log("Created LV1 Bridge controller"); return result;
}
extern "C" __attribute__((visibility("default"))) int WMSD_SetPreset(void* controller,const char* data,unsigned size,const char* version) {
    auto fn = (Setter)Resolve("WMSD_SetPreset"); if (!fn) return -1;
    std::string value = Normalize(data, size); int rc = fn(controller, value.c_str(), (unsigned)value.size() + 1, version);
    if (!rc && controller) { std::lock_guard<std::mutex> lock(presetMutex); presets[controller] = value; }
    Log(rc == 0 ? "Applied fixed FaderBank MIDI binding" : "WMSD_SetPreset failed"); return rc;
}
extern "C" __attribute__((visibility("default"))) Word WMSD_InitControllerForType(Word a,Word b,Word c,Word d,Word e,Word f,Word g,Word h) {
    if (a) {
        std::string value = DefaultPreset;
        { std::lock_guard<std::mutex> lock(presetMutex); auto it = presets.find((void*)a); if (it != presets.end()) value = it->second; }
        auto setter = (Setter)Resolve("WMSD_SetPreset"); if (setter) setter((void*)a, value.c_str(), (unsigned)value.size() + 1, "");
    }
    auto fn = (Generic)Resolve("WMSD_InitControllerForType"); return fn ? fn(a,b,c,d,e,f,g,h) : 0;
}
extern "C" __attribute__((visibility("default"))) Word WMSD_DestroyController(Word a,Word b,Word c,Word d,Word e,Word f,Word g,Word h) {
    { std::lock_guard<std::mutex> lock(presetMutex); presets.erase((void*)a); }
    auto fn = (Generic)Resolve("WMSD_DestroyController"); return fn ? fn(a,b,c,d,e,f,g,h) : 0;
}
extern "C" __attribute__((visibility("default"))) int WMSD_ShowConfigWindow(void*) { return 0; }

#define FORWARD(name) extern "C" __attribute__((visibility("default"))) Word name(Word a,Word b,Word c,Word d,Word e,Word f,Word g,Word h) { auto fn=(Generic)Resolve(#name); return fn ? fn(a,b,c,d,e,f,g,h) : 0; }
FORWARD(WMSD_CommandStatusUpdate)
FORWARD(WMSD_ControllerDisplayUpdate)
FORWARD(WMSD_GetAvailableControllerInfo)
FORWARD(WMSD_GetPreset)
FORWARD(WMSD_GetTypeForController)
FORWARD(WMSD_IdentifyController)
FORWARD(WMSD_LockConfigWindow)
FORWARD(WMSD_MixerAssignmentsChanged)
FORWARD(WMSD_MixerMessageSent)
FORWARD(WMSD_MixerParamWasChanged)
FORWARD(WMSD_QueryInterfaceVersion)
FORWARD(WMSD_TestControllerForType)
FORWARD(WMSD_UnlockConfigWindow)
