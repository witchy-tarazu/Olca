using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using Olca.Unity;
using Olca.Application.Management;
using Olca.Presentation.Field;
using Olca.Presentation.Dungeon;
namespace Olca.Tests.Application
{
    public sealed class CameraPresentationManagementTests
    {
        private readonly List<(Behaviour Value,bool Enabled)> previous=new List<(Behaviour,bool)>();
        private GameObject root;
        private ResourceManager resources;
        private CameraRuntimeManager cameras;
        [SetUp] public void SetUp()
        {
            foreach(var camera in UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsInactive.Include,FindObjectsSortMode.None)){previous.Add((camera,camera.enabled));camera.enabled=false;}
            foreach(var listener in UnityEngine.Object.FindObjectsByType<AudioListener>(FindObjectsInactive.Include,FindObjectsSortMode.None)){previous.Add((listener,listener.enabled));listener.enabled=false;}
            root=new GameObject("CameraManagerTests");resources=new ResourceManager();cameras=new CameraRuntimeManager(resources,root.transform);
        }
        [TearDown] public void TearDown(){cameras.Dispose();resources.Dispose();UnityEngine.Object.DestroyImmediate(root);foreach(var item in previous)if(item.Value!=null)item.Value.enabled=item.Enabled;previous.Clear();}
        [Test] public void Rental_排他所有と返却再借用で位置投影描画設定を復元する()
        {
            var preset=Resources.Load<GameObject>("OlcaSystem/GameplayCamera");var baseline=preset.GetComponent<Camera>();var owner=new object();var rental=cameras.Borrow(owner);var camera=rental.Camera;int id=camera.GetInstanceID();
            Assert.AreSame(camera,Camera.main);Assert.Throws<InvalidOperationException>(()=>cameras.Borrow(owner));Assert.Throws<InvalidOperationException>(()=>cameras.Borrow(new object()));
            var extra=camera.GetComponents<MonoBehaviour>().Select(x=>JsonUtility.ToJson(x)).ToArray();
            camera.transform.localPosition=new Vector3(50,60,70);camera.transform.localRotation=Quaternion.Euler(0,0,30);camera.orthographicSize=40;camera.cullingMask=0;camera.aspect=.5f;camera.projectionMatrix=Matrix4x4.identity;
            var texture=new RenderTexture(16,16,0);camera.targetTexture=texture;camera.GetComponent<AudioListener>().enabled=false;
            try {
                rental.Dispose();Assert.IsFalse(cameras.IsBorrowed);Assert.IsNull(rental.Camera);Assert.IsFalse(camera.gameObject.activeSelf);Assert.AreEqual(baseline.orthographicSize,camera.orthographicSize);Assert.AreEqual(baseline.cullingMask,camera.cullingMask);Assert.IsNull(camera.targetTexture);Assert.AreEqual(preset.transform.localPosition,camera.transform.localPosition);Assert.AreEqual(preset.transform.localRotation,camera.transform.localRotation);Assert.IsTrue(camera.GetComponent<AudioListener>().enabled);
                var next=cameras.Borrow(new object());Assert.AreEqual(id,next.Camera.GetInstanceID());Assert.AreNotEqual(Matrix4x4.identity,next.Camera.projectionMatrix);CollectionAssert.AreEqual(extra,next.Camera.GetComponents<MonoBehaviour>().Select(x=>JsonUtility.ToJson(x)).ToArray());rental.Dispose();Assert.IsTrue(cameras.IsBorrowed);next.Dispose();next.Dispose();Assert.IsFalse(cameras.IsBorrowed);Assert.AreEqual(1,resources.OwnedInstanceCount);
            } finally {UnityEngine.Object.DestroyImmediate(texture);}
        }
        [Test] public void Rental_外部カメラやListenerを重複生成しない()
        {
            var foreign=new GameObject("ForeignCamera",typeof(Camera),typeof(AudioListener));
            try {Assert.Throws<InvalidOperationException>(()=>cameras.Borrow(new object()));Assert.AreEqual(0,resources.OwnedInstanceCount);foreign.GetComponent<Camera>().enabled=false;Assert.Throws<InvalidOperationException>(()=>cameras.Borrow(new object()));Assert.AreEqual(0,resources.OwnedInstanceCount);}
            finally {UnityEngine.Object.DestroyImmediate(foreign);}
        }
        [Test] public void Binding_明示束縛解除して互換カメラへ戻してから排他権を返却する()
        {
            var legacyObject=new GameObject("LegacyCamera",typeof(Camera),typeof(AudioListener));legacyObject.tag="MainCamera";var legacy=legacyObject.GetComponent<Camera>();var fieldObject=new GameObject("Field");var field=fieldObject.AddComponent<GameFieldView>();var backgroundObject=new GameObject("Background");var background=backgroundObject.AddComponent<ScrollingBackgroundView>();field.ConfigurePresentationCamera(legacy);
            try {
                var binding=new GameplayCameraBinding(new object(),cameras,field,legacy,new[]{background});Assert.IsFalse(legacy.enabled);Assert.IsFalse(legacyObject.GetComponent<AudioListener>().enabled);Assert.AreSame(cameras.BorrowedCamera,field.PresentationCamera);Assert.AreSame(field.PresentationCamera,background.PresentationCamera);Assert.AreSame(field.PresentationCamera,Camera.main);
                binding.Dispose();binding.Dispose();Assert.AreSame(legacy,field.PresentationCamera);Assert.IsNull(background.PresentationCamera);Assert.IsTrue(legacy.enabled);Assert.IsTrue(legacyObject.GetComponent<AudioListener>().enabled);Assert.IsFalse(cameras.IsBorrowed);Assert.AreSame(legacy,Camera.main);
            } finally {UnityEngine.Object.DestroyImmediate(backgroundObject);UnityEngine.Object.DestroyImmediate(fieldObject);UnityEngine.Object.DestroyImmediate(legacyObject);}
        }
        [Test] public void HudPrefab_参照を内包して従来のCanvas設定と字体を保持する()
        {
            var prefab=Resources.Load<GameObject>("OlcaSystem/DungeonHud");Assert.NotNull(prefab);var hud=prefab.GetComponent<DungeonHudView>();Assert.NotNull(hud);Assert.NotNull(hud.CanvasGroup);Assert.AreEqual(5,prefab.GetComponent<Canvas>().sortingOrder);Assert.AreEqual(1,prefab.GetComponent<CanvasScaler>().matchWidthOrHeight);
            var text=prefab.GetComponentsInChildren<Text>(true).Single(x=>x.name=="DungeonCardCounts");Assert.NotNull(text.font);Assert.IsTrue(text.transform.IsChildOf(prefab.transform));Assert.NotNull(prefab.transform.Find("DungeonDebugText"));Assert.NotNull(prefab.transform.Find("PlayerStatusText"));
        }
    }
}
