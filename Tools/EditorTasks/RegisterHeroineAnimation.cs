using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using Olca.Presentation.Field;

// Unity CLIのrun_scriptから実行する登録処理。Assets外に置き、通常のゲームビルドには含めない。
public static class RegisterHeroineAnimation
{
    private const string Folder = "Assets/Art/Animations/Player";

    public static object Main()
    {
        if (EditorApplication.isPlaying)
            throw new InvalidOperationException("Play Modeを終了してから登録してください。");

        var view = UnityEngine.Object.FindFirstObjectByType<GameFieldView>();
        if (view == null || view.PlayerSprite == null)
            throw new InvalidOperationException("GameFieldViewとPlayerSpriteがあるダンジョンシーンを開いてください。");

        var frames = Enumerable.Range(1, 10)
            .Select(index => LoadSprite($"Assets/Art/Sprites/Characters/Heroine/Idle/heroine_idle_{index:D2}.png"))
            .ToArray();
        var serializedView = new SerializedObject(view);
        var dash = serializedView.FindProperty("dashPlayerSprite").objectReferenceValue as Sprite;
        if (dash == null) throw new InvalidOperationException("ダッシュ用Spriteが未設定です。");

        EnsureFolder("Assets/Art", "Animations");
        EnsureFolder("Assets/Art/Animations", "Player");
        var idleClip = CreateClip("Player_Idle", frames);
        var dashClip = CreateClip("Player_Dash", new[] { dash });
        string controllerPath = Folder + "/PlayerAnimator.controller";
        if (AssetDatabase.LoadMainAssetAtPath(controllerPath) != null)
            throw new InvalidOperationException("同名Controllerが存在します。上書きを避けて停止しました。");

        var controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
        controller.AddParameter("isRunning", AnimatorControllerParameterType.Bool);
        var stateMachine = controller.layers[0].stateMachine;
        var idle = stateMachine.AddState("Idle");
        idle.motion = idleClip;
        var running = stateMachine.AddState("Dash");
        running.motion = dashClip;
        stateMachine.defaultState = idle;
        AddTransition(idle, running, AnimatorConditionMode.If);
        AddTransition(running, idle, AnimatorConditionMode.IfNot);

        var animator = view.PlayerSprite.GetComponent<Animator>();
        if (animator == null) animator = Undo.AddComponent<Animator>(view.PlayerSprite.gameObject);
        Undo.RecordObject(animator, "プレイヤーアニメーション登録");
        animator.runtimeAnimatorController = controller;
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        Undo.RecordObject(view, "プレイヤーAnimator参照設定");
        serializedView.FindProperty("playerAnimator").objectReferenceValue = animator;
        serializedView.ApplyModifiedProperties();
        Undo.RecordObject(view.PlayerSprite, "待機アニメーション初期画像設定");
        view.PlayerSprite.sprite = frames[0];
        EditorSceneManager.MarkSceneDirty(view.gameObject.scene);
        AssetDatabase.SaveAssets();
        if (!EditorSceneManager.SaveScene(view.gameObject.scene))
            throw new InvalidOperationException("シーンを保存できませんでした。");

        return new { idle = AssetDatabase.GetAssetPath(idleClip), fps = idleClip.frameRate,
            duration = idleClip.length, frames = frames.Length, controller = controllerPath,
            scene = view.gameObject.scene.path };
    }

    private static Sprite LoadSprite(string path)
    {
        var sprites = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().ToArray();
        if (sprites.Length != 1)
            throw new InvalidOperationException($"Spriteが1個の状態で取り込んでください: {path}");
        return sprites[0];
    }

    private static void EnsureFolder(string parent, string name)
    {
        if (!AssetDatabase.IsValidFolder(parent + "/" + name))
            AssetDatabase.CreateFolder(parent, name);
    }

    private static AnimationClip CreateClip(string name, Sprite[] frames)
    {
        string path = Folder + "/" + name + ".anim";
        if (AssetDatabase.LoadMainAssetAtPath(path) != null)
            throw new InvalidOperationException($"同名Clipが存在します: {path}");

        var clip = new AnimationClip { name = name, frameRate = 10f };
        // Spriteの最終キーにも1フレーム分の長さが付くため、末尾の重複キーは追加しない。
        var keys = new ObjectReferenceKeyframe[frames.Length];
        for (int index = 0; index < keys.Length; index++)
            keys[index] = new ObjectReferenceKeyframe { time = index / 10f, value = frames[index] };
        var binding = EditorCurveBinding.PPtrCurve("", typeof(SpriteRenderer), "m_Sprite");
        AnimationUtility.SetObjectReferenceCurve(clip, binding, keys);
        var settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = true;
        AnimationUtility.SetAnimationClipSettings(clip, settings);
        AssetDatabase.CreateAsset(clip, path);
        return clip;
    }

    private static void AddTransition(AnimatorState from, AnimatorState to, AnimatorConditionMode condition)
    {
        var transition = from.AddTransition(to);
        transition.hasExitTime = false;
        transition.duration = 0f;
        transition.AddCondition(condition, 0f, "isRunning");
    }
}
