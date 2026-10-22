using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using Olca.Core.Domain;

namespace Olca.Core.Logic.Dungeon
{
    /// <summary>既存保存の封筒形式。同じcodecをUnity保存層と純C#読取で共有し、I/Oは持たない。</summary>
    public static class PortableCheckpointJson
    {
        [DataContract] private sealed class Envelope
        {
            [DataMember(IsRequired=true)] public int Version;
            [DataMember(IsRequired=true)] public string Payload;
            [DataMember(IsRequired=true)] public string Sha256;
        }
        public static byte[] Serialize<T>(T value)
        { using(var stream=new MemoryStream()){new DataContractJsonSerializer(typeof(T)).WriteObject(stream,value);return stream.ToArray();} }
        public static T Deserialize<T>(byte[] bytes)
        { if(bytes==null||bytes.Length==0||bytes.Length>16*1024*1024)throw new InvalidDataException("データサイズが不正です");using(var stream=new MemoryStream(bytes))return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(stream); }
        public static string Hash(byte[] bytes)
        { using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-",""); }
        public static byte[] Encode(CheckpointDto value)
        {var payload=Serialize(value);return Serialize(new Envelope{Version=1,Payload=Encoding.UTF8.GetString(payload),Sha256=Hash(payload)});}
        public static byte[] Encode(FloorSequenceCheckpointDto value)
        {var payload=Serialize(value);return Serialize(new Envelope{Version=1,Payload=Encoding.UTF8.GetString(payload),Sha256=Hash(payload)});}
        public static FloorSequenceCheckpointDto DecodeFloor(byte[] bytes)
        {
            var e=Deserialize<Envelope>(bytes);
            if(e==null||e.Version!=1||e.Payload==null||Hash(Encoding.UTF8.GetBytes(e.Payload))!=e.Sha256)throw new InvalidDataException("保存形式が未対応、または整合性検証に失敗しました");
            return Deserialize<FloorSequenceCheckpointDto>(Encoding.UTF8.GetBytes(e.Payload));
        }
        public static CheckpointDto Decode(byte[] bytes)
        {
            var e=Deserialize<Envelope>(bytes);
            if(e==null||e.Version!=1||e.Payload==null||Hash(Encoding.UTF8.GetBytes(e.Payload))!=e.Sha256)throw new InvalidDataException("保存形式が未対応、または整合性検証に失敗しました");
            return Deserialize<CheckpointDto>(Encoding.UTF8.GetBytes(e.Payload));
        }
    }
}
