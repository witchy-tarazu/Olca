using System;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
namespace Olca.Unity
{
    /// <summary>外部の明示値だけを出力する。ゲーム内の発生契機・製品プリセットは登録しない。</summary>
    public sealed class GamepadVibrationRequest
    {
        public float Low { get; }
        public float High { get; }
        public double Seconds { get; }
        public GamepadVibrationRequest(float low,float high,double seconds)
        {
            if(float.IsNaN(low)||float.IsInfinity(low)||float.IsNaN(high)||float.IsInfinity(high)||low<0||low>1||high<0||high>1||double.IsNaN(seconds)||double.IsInfinity(seconds)||seconds<=0)throw new ArgumentOutOfRangeException("振動の明示値が不正です");
            Low=low;High=high;Seconds=seconds;
        }
    }
    public sealed class GamepadVibrationAdapter:IDisposable
    {
        private readonly Func<double> clock;
        private readonly Action<Gamepad,float,float> output;
        private Gamepad active;
        private double endsAt;
        private bool enabled,disposed;
        public bool IsPlaying=>active!=null;
        public bool Enabled {get=>enabled;set{enabled=value;if(!value)Stop();}}
        public GamepadVibrationAdapter():this(()=>Time.realtimeSinceStartupAsDouble,(pad,low,high)=>pad.SetMotorSpeeds(low,high)){}
        public GamepadVibrationAdapter(Func<double> time,Action<Gamepad,float,float> sink)
        {
            clock=time??throw new ArgumentNullException(nameof(time));output=sink??throw new ArgumentNullException(nameof(sink));
            InputSystem.onDeviceChange+=DeviceChanged;
        }
        public bool TryPlay(GamepadVibrationRequest request)=>TryPlay(Gamepad.current,request);
        public bool TryPlay(Gamepad device,GamepadVibrationRequest request)
        {
            Stop();if(disposed||!enabled||device==null||!device.added||!device.enabled||request==null||request.Low==0&&request.High==0)return false;
            double now=clock(),end=now+request.Seconds;
            if(double.IsNaN(now)||double.IsInfinity(now)||double.IsInfinity(end))return false;
            active=device;endsAt=end;output(device,request.Low,request.High);return true;
        }
        public void Tick(){if(active==null)return;double now=clock();if(!active.added||!active.enabled||double.IsNaN(now)||double.IsInfinity(now)||now>=endsAt)Stop();}
        public void Stop(){var device=active;active=null;endsAt=0;if(device!=null)output(device,0,0);}
        private void DeviceChanged(InputDevice device,InputDeviceChange change)
        {if(ReferenceEquals(active,device)&&(change==InputDeviceChange.Removed||change==InputDeviceChange.Disconnected||change==InputDeviceChange.Disabled))Stop();}
        public void Dispose(){if(disposed)return;Stop();disposed=true;InputSystem.onDeviceChange-=DeviceChanged;}
    }
    public sealed class GamepadVibrationSettingsStore
    {
        [System.Runtime.Serialization.DataContract] private sealed class Document {
            [System.Runtime.Serialization.DataMember(IsRequired=true)] public int Version;
            [System.Runtime.Serialization.DataMember(IsRequired=true)] public bool Enabled;
        }
        private readonly string directory;
        private readonly CheckpointFileIo io;
        private bool writing;
        public string ActivePath => System.IO.Path.Combine(directory,"gamepad-vibration.json");
        private string BackupPath => ActivePath+".backup";
        public GamepadVibrationSettingsStore(string directory,CheckpointFileIo io=null)
        {this.directory=System.IO.Path.GetFullPath(directory);this.io=io??new CheckpointFileIo();}
        private static byte[] Encode(bool enabled){using(var stream=new System.IO.MemoryStream()){
            new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(Document)).WriteObject(stream,new Document{Version=1,Enabled=enabled});return stream.ToArray();}}
        private static bool Decode(byte[] bytes){if(bytes.Length==0||bytes.Length>4096)throw new System.IO.InvalidDataException();
            using(var stream=new System.IO.MemoryStream(bytes)){var doc=(Document)new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(Document)).ReadObject(stream);
                if(doc==null||doc.Version!=1)throw new System.IO.InvalidDataException();return doc.Enabled;}}
        private static bool Failure(System.Exception e)=>e is System.IO.IOException||e is System.UnauthorizedAccessException||e is System.Runtime.Serialization.SerializationException||e is System.Xml.XmlException||e is System.ArgumentException||e is System.Security.SecurityException;
        public bool TryRead(out bool enabled,out string error){enabled=false;error=null;try{
            if(!System.IO.File.Exists(ActivePath)){if(System.IO.Directory.Exists(ActivePath)||System.IO.File.Exists(BackupPath))throw new System.IO.IOException();return true;}
            enabled=Decode(System.IO.File.ReadAllBytes(ActivePath));return true;
        }catch(System.Exception e)when(Failure(e)){error="振動の設定を読めません。保存内容は変更していません。";return false;}}
        public bool TrySave(bool enabled,out string error){error=null;if(!io.TryRequireWritable(ActivePath,BackupPath,out error))return false;if(writing){error="振動設定を保存中です";return false;}if(!TryRead(out _,out error))return false;
            var bytes=Encode(enabled);string temporary=null;writing=true;try{
                if(AtomicSettingsWrite.TryCommit(io,directory,ActivePath,BackupPath,".vibration-",bytes,b=>{Decode(b);},Failure,out temporary))return true;
                error="振動設定を保存できません。現在の設定を維持します。";return false;
            }finally{writing=false;AtomicSettingsWrite.Cleanup(temporary,Failure);}

        }
    }
}
