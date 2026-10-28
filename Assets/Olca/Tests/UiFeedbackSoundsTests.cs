using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Olca.Unity;
namespace Olca.Tests
{
    public class UiFeedbackSoundsTests
    {
        [TestCase("press",80)] [TestCase("cancel",96)]
        public void 自作UI音は短い単音PCMで低ピークかつ両端ゼロと微小DCを保つ(string name,int milliseconds)
        {
            var clip=Resources.Load<AudioClip>("OlcaUiSounds/"+name);Assert.IsNotNull(clip);Assert.AreEqual(1,clip.channels);Assert.AreEqual(48000,clip.frequency);Assert.AreEqual(milliseconds*48,clip.samples);
            var bytes=File.ReadAllBytes(Path.Combine(UnityEngine.Application.dataPath,"Resources/OlcaUiSounds",name+".wav"));
            using(var input=new BinaryReader(new MemoryStream(bytes))){Assert.AreEqual("RIFF",new string(input.ReadChars(4)));input.ReadInt32();Assert.AreEqual("WAVE",new string(input.ReadChars(4)));Assert.AreEqual("fmt ",new string(input.ReadChars(4)));Assert.AreEqual(16,input.ReadInt32());Assert.AreEqual(1,input.ReadInt16());Assert.AreEqual(1,input.ReadInt16());Assert.AreEqual(48000,input.ReadInt32());input.ReadBytes(6);Assert.AreEqual(16,input.ReadInt16());Assert.AreEqual("data",new string(input.ReadChars(4)));int length=input.ReadInt32()/2;var samples=Enumerable.Range(0,length).Select(_=>input.ReadInt16()/32768.0).ToArray();
                Assert.AreEqual(0,samples[0]);Assert.AreEqual(0,samples.Last());Assert.LessOrEqual(samples.Max(System.Math.Abs),.101);Assert.Greater(samples.Max(System.Math.Abs),.09);Assert.Less(System.Math.Abs(samples.Average()),.00001);Assert.Less(System.Math.Abs(samples[1]),.0001);Assert.Less(System.Math.Abs(samples[length-2]),.0001);}
        }
        [Test] public void 明示Clipは全ボタンで優先し既知取消だけ別音を共有する()
        {
            var press=UiFeedbackSounds.ForButton("ConfirmRetreat");var cancel=UiFeedbackSounds.ForButton("CancelRetreat");Assert.IsNotNull(press);Assert.IsNotNull(cancel);Assert.AreNotSame(press,cancel);Assert.AreSame(press,UiFeedbackSounds.ForButton(null));Assert.AreSame(press,UiFeedbackSounds.ForButton("ResumeButton"));Assert.AreSame(cancel,UiFeedbackSounds.ForButton("CancelProgressAction"));
            var custom=AudioClip.Create("Explicit",48,1,48000,false);try{Assert.AreSame(custom,UiFeedbackSounds.ForButton("CancelRetreat",custom));Assert.AreSame(custom,UiFeedbackSounds.ForButton("Other",custom));}finally{Object.DestroyImmediate(custom);}Assert.IsNotNull(press);Assert.AreSame(press,UiFeedbackSounds.ForButton("Other"));
        }
        [Test] public void 仮音も既存音量と休止経路を使い破棄で所有ソースだけを解放する()
        {
            var host=new GameObject("UiSoundTest");var external=host.AddComponent<AudioSource>();external.volume=.4f;var router=new GameplayAudioRouter(host.transform);var effects=router.Effects;
            try{router.Register(external,GameplayAudioChannel.Effects);router.Apply(new AudioVolumeSettings(20,100,50,100));Assert.AreEqual(.1f,effects.volume,.00001f);Assert.AreEqual(.04f,external.volume,.00001f);router.SetPaused(true);router.PlayEffect(UiFeedbackSounds.ForButton("Other"));Assert.IsFalse(effects.isPlaying);router.SetPaused(false);router.Dispose();Assert.IsTrue(effects==null);Assert.IsNotNull(external);Assert.AreEqual(.4f,external.volume,.00001f);Assert.IsNotNull(UiFeedbackSounds.ForButton("Other"));router.PlayEffect(UiFeedbackSounds.ForButton("Other"));}finally{router.Dispose();Object.DestroyImmediate(host);}
        }
    }
}
