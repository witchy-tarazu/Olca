using System;
using System.Linq;
using UnityEngine;
using Olca.Unity;
using Olca.Presentation.Field;
namespace Olca.Application.Management
{
    /// <summary>ホストの既存参照を貸出カメラへ束縛し、返却前に旧Viewから参照を外す。</summary>
    public sealed class GameplayCameraBinding : IDisposable
    {
        private CameraRuntimeManager.Rental rental;
        private readonly GameFieldView field;
        private readonly Camera previousFieldCamera,legacy;
        private readonly AudioListener listener;
        private readonly bool cameraEnabled,listenerEnabled;
        private readonly ScrollingBackgroundView[] backgrounds;
        private readonly Camera[] previousBackgroundCameras;
        private bool disposed;
        public GameplayCameraBinding(object owner,CameraRuntimeManager cameras,GameFieldView field,Camera legacy,ScrollingBackgroundView[] backgrounds)
        {
            this.field=field??throw new ArgumentNullException(nameof(field));this.legacy=legacy??throw new ArgumentNullException(nameof(legacy));
            this.backgrounds=backgrounds??Array.Empty<ScrollingBackgroundView>();previousBackgroundCameras=this.backgrounds.Select(x=>x.PresentationCamera).ToArray();previousFieldCamera=field.PresentationCamera;
            listener=legacy.GetComponent<AudioListener>();cameraEnabled=legacy.enabled;listenerEnabled=listener!=null&&listener.enabled;
            legacy.enabled=false;if(listener!=null)listener.enabled=false;
            try{rental=cameras.Borrow(owner);field.ConfigurePresentationCamera(rental.Camera);foreach(var background in this.backgrounds)background.ConfigurePresentationCamera(rental.Camera);}
            catch{Dispose();throw;}
        }
        public void Dispose()
        {
            if(disposed)return;
            if(!ReferenceEquals(field,null))field.ConfigurePresentationCamera(previousFieldCamera);
            for(int i=0;i<backgrounds.Length;i++)if(!ReferenceEquals(backgrounds[i],null))backgrounds[i].ConfigurePresentationCamera(previousBackgroundCameras[i]);
            rental?.Dispose();rental=null;disposed=true;
            if(legacy!=null)legacy.enabled=cameraEnabled;if(listener!=null)listener.enabled=listenerEnabled;
        }
    }
}
