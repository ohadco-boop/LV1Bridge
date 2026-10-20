using System;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace FaderBankXTouchBridge
{
    internal sealed class MeterWindow : Form
    {
        readonly Func<string> read;
        readonly Timer refresh = new Timer();
        readonly string settings = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"LV1 Bridge","meter-window.txt");
        string[] names=new string[16]; int[] levels=new int[32]; bool fresh; string previous;
        bool[] mono=new bool[16];
        Rectangle framedBounds; bool borderless;
        bool autoFit=true; int stripWidth=72, stripGap=8, leftOffset=0;
        Form layoutWindow;
        public MeterWindow(Func<string> snapshot)
        {
            read=snapshot; Text="LV1 — 16 Channel Meters"; BackColor=Color.FromArgb(8,15,24); ForeColor=Color.White;
            DoubleBuffered=true; Size=new Size(1280,240); MinimumSize=new Size(640,120); StartPosition=FormStartPosition.Manual;
            try { string[] p=File.ReadAllText(settings).Split(','); Rectangle r=new Rectangle(int.Parse(p[0]),int.Parse(p[1]),int.Parse(p[2]),int.Parse(p[3])); foreach(Screen s in Screen.AllScreens) if(s.WorkingArea.IntersectsWith(r)) { Bounds=r; break; } if(p.Length>=8) {autoFit=p[4]=="1";stripWidth=Math.Max(24,Math.Min(500,int.Parse(p[5])));stripGap=Math.Max(0,Math.Min(300,int.Parse(p[6])));leftOffset=Math.Max(0,Math.Min(10000,int.Parse(p[7])));} } catch { Location=new Point(50,50); }
            var menu=new ContextMenuStrip();
            var top=new ToolStripMenuItem("Always on top"); top.CheckOnClick=true; top.CheckedChanged+=delegate {TopMost=top.Checked;}; menu.Items.Add(top);
            menu.Items.Add("Fill this display / restore (double-click)",null,delegate {ToggleFill();});
            menu.Items.Add("Meter width / spacing...",null,delegate {ShowLayout();});
            menu.Items.Add("Close meters",null,delegate {Close();}); ContextMenuStrip=menu;
            MouseDoubleClick+=delegate(object s,MouseEventArgs e) {if(e.Button==MouseButtons.Left)ToggleFill();};
            refresh.Interval=80; refresh.Tick+=delegate {UpdateMeters();}; refresh.Start();
            FormClosed+=delegate { refresh.Dispose(); if(layoutWindow!=null)layoutWindow.Close(); SaveLayout(); };
            Resize+=delegate {Invalidate();}; UpdateMeters();
        }
        void SaveLayout()
        {
            try {Rectangle r=borderless?framedBounds:Bounds; Directory.CreateDirectory(Path.GetDirectoryName(settings)); File.WriteAllText(settings,String.Join(",",r.X,r.Y,r.Width,r.Height,autoFit?1:0,stripWidth,stripGap,leftOffset));} catch {}
        }
        void ShowLayout()
        {
            if(layoutWindow!=null && !layoutWindow.IsDisposed) {layoutWindow.Activate();return;}
            Form dialog=new Form {Text="Meter layout — pixels",ClientSize=new Size(360,232),FormBorderStyle=FormBorderStyle.FixedDialog,MaximizeBox=false,MinimizeBox=false,StartPosition=FormStartPosition.CenterParent};
            layoutWindow=dialog;
            CheckBox fit=new CheckBox {Text="Fit all 16 meters to window",Checked=autoFit,Left=18,Top=16,Width=310};dialog.Controls.Add(fit);
            NumericUpDown[] values=new NumericUpDown[3]; string[] titles={"Meter width (px)","Gap between meters (px)","Left offset (px)"}; int[] current={stripWidth,stripGap,leftOffset}; int[] maximum={500,300,10000};
            for(int i=0;i<3;i++) {
                dialog.Controls.Add(new Label {Text=titles[i],Left=18,Top=54+i*36,Width=210});
                values[i]=new NumericUpDown {Left=244,Top=50+i*36,Width=94,Minimum=i==0?24:0,Maximum=maximum[i],Value=current[i],Enabled=!autoFit};dialog.Controls.Add(values[i]);
            }
            Label note=new Label {Text="Changes preview immediately and are saved.",Left=18,Top=167,Width=325,Height=22};dialog.Controls.Add(note);
            EventHandler update=delegate {autoFit=fit.Checked;stripWidth=(int)values[0].Value;stripGap=(int)values[1].Value;leftOffset=(int)values[2].Value;foreach(var value in values)value.Enabled=!autoFit;Invalidate();SaveLayout();};
            fit.CheckedChanged+=update;foreach(var value in values)value.ValueChanged+=update;
            Button done=new Button {Text="Done",Left=244,Top=195,Width=94};done.Click+=delegate {dialog.Close();};dialog.Controls.Add(done);dialog.Show(this);
        }
        void ToggleFill() { if(!borderless) {framedBounds=Bounds; Rectangle screen=Screen.FromControl(this).Bounds; FormBorderStyle=FormBorderStyle.None; Bounds=screen;} else {FormBorderStyle=FormBorderStyle.Sizable; Bounds=framedBounds;} borderless=!borderless; }
        void UpdateMeters()
        {
            string value=read(); if(value==previous)return; previous=value; string[] p=value.Split('|'); if(p.Length<50)return; fresh=p[1]=="1";
            for(int i=0;i<16;i++)mono[i]=p.Length>=66 && p[50+i]=="1";
            for(int i=0;i<16;i++) {names[i]=Encoding.UTF8.GetString(Convert.FromBase64String(p[2+i*3])); int.TryParse(p[3+i*3],out levels[i*2]); int.TryParse(p[4+i*3],out levels[i*2+1]);} Invalidate();
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); float width=autoFit?ClientSize.Width/16f:stripWidth; float gap=autoFit?0:stripGap; int offset=autoFit?0:leftOffset; int bottom=ClientSize.Height-24; int top=34; int height=Math.Max(20,bottom-top);
            using(Font label=new Font("Segoe UI",Math.Max(8,Math.Min(16,width/6)),FontStyle.Bold))
            using(Font small=new Font("Segoe UI",8))
            using(var format=new StringFormat {Alignment=StringAlignment.Center,LineAlignment=StringAlignment.Center,Trimming=StringTrimming.EllipsisCharacter,FormatFlags=StringFormatFlags.NoWrap})
            {
                for(int i=0;i<16;i++)
                {
                    float x=offset+i*(width+gap); e.Graphics.DrawLine(Pens.DimGray,x,0,x,ClientSize.Height);
                    e.Graphics.DrawString(names[i]??"",label,Brushes.White,new RectangleF(x+3,0,width-6,32),format);
                    for(int lane=0;lane<(mono[i]?1:2);lane++) for(int n=0;n<24;n++)
                    {
                        bool lit=fresh && n<(levels[i*2+lane]*24+126)/127;
                        Brush color=lit?(n>=22?Brushes.Red:n>=18?Brushes.Gold:Brushes.LimeGreen):Brushes.DarkSlateGray;
                        e.Graphics.FillRectangle(color,x+width*.19f+lane*width*.32f,top+height-(n+1)*height/24f,width*(mono[i]?.58f:.26f),Math.Max(1,height/24f-2));
                    }
                    e.Graphics.DrawString(fresh?(mono[i]?"MONO  "+(i+1):"L   "+(i+1)+"   R"):"NO DATA",small,Brushes.LightGray,new RectangleF(x,bottom,width,24),format);
                }
            }
        }
    }
}
