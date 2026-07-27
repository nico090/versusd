using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace VersusD.EditorTools
{
    /// <summary>
    /// Genera el proyecto Xcode del cliente iOS. Unity no compila el .ipa: escupe un
    /// proyecto Xcode que despues se abre y se corre desde Xcode contra el iPhone.
    ///
    /// Uso normal:  menu  VersusD/Build/iOS - proyecto Xcode
    /// Uso por CLI:
    ///   /Applications/Unity/Hub/Editor/6000.0.52f1/Unity.app/Contents/MacOS/Unity \
    ///     -quit -batchmode -nographics -projectPath &lt;proyecto&gt; \
    ///     -executeMethod VersusD.EditorTools.IOSBuild.BuildCommandLine
    ///
    /// El equipo de firma se toma de la variable de entorno APPLE_TEAM_ID si existe;
    /// si no, queda vacio y se elige a mano en Xcode (Signing &amp; Capabilities).
    /// </summary>
    public static class IOSBuild
    {
        /// Bundle id del cliente iOS. Cambiar aca si se registra otro en el portal de Apple.
        public const string BundleIdentifier = "com.nico.versusd";

        /// Salida relativa a la carpeta del proyecto Unity (esta en .gitignore).
        const string k_OutputFolder = "iosbuild";

        /// iOS 13 es el minimo que soporta Unity 6; Xcode 26 lo sigue aceptando.
        const string k_MinimumOSVersion = "13.0";

        [MenuItem("VersusD/Build/iOS - proyecto Xcode")]
        public static void BuildFromMenu()
        {
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.iOS)
            {
                // Cambiar de target dispara recompilacion del dominio: el post-process de
                // iOS (que esta bajo #if UNITY_IOS) recien existe despues de eso.
                if (!EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.iOS, BuildTarget.iOS))
                {
                    EditorUtility.DisplayDialog("Build iOS",
                        "No se pudo cambiar el target a iOS. Falta el modulo iOS Build Support en el Hub.",
                        "Ok");
                    return;
                }

                EditorUtility.DisplayDialog("Build iOS",
                    "Cambie el target a iOS y Unity va a recompilar los scripts.\n\n" +
                    "Cuando termine, volve a correr VersusD/Build/iOS - proyecto Xcode.",
                    "Ok");
                return;
            }

            var report = Build();
            if (report.summary.result == BuildResult.Succeeded)
            {
                EditorUtility.RevealInFinder(OutputPath);
            }
        }

        /// Entrada para batchmode: sale con codigo != 0 si el build falla.
        public static void BuildCommandLine()
        {
            var report = Build();
            EditorApplication.Exit(report.summary.result == BuildResult.Succeeded ? 0 : 1);
        }

        public static string OutputPath =>
            Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), k_OutputFolder));

        static BuildReport Build()
        {
            ApplyPlayerSettings();

            var scenes = EditorBuildSettings.scenes
                .Where(scene => scene.enabled)
                .Select(scene => scene.path)
                .ToArray();

            if (scenes.Length == 0)
            {
                throw new InvalidOperationException(
                    "No hay escenas habilitadas en Build Settings; el build iOS quedaria vacio.");
            }

            Directory.CreateDirectory(OutputPath);

            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = OutputPath,
                target = BuildTarget.iOS,
                targetGroup = BuildTargetGroup.iOS,
                // Append reusa el proyecto Xcode anterior (mucho mas rapido en rebuilds) y
                // conserva lo que uno haya tocado a mano en Xcode.
                options = BuildOptions.AcceptExternalModificationsToPlayer,
            };

            Debug.Log($"[IOSBuild] Generando proyecto Xcode en {OutputPath}");
            var report = BuildPipeline.BuildPlayer(options);
            Debug.Log($"[IOSBuild] Resultado: {report.summary.result} " +
                      $"({report.summary.totalTime.TotalMinutes:F1} min)");
            return report;
        }

        /// Todo lo que el build iOS necesita si o si. Se aplica en cada build para que no
        /// dependa de que alguien se acuerde de tocarlo en el inspector.
        static void ApplyPlayerSettings()
        {
            var iOS = NamedBuildTarget.iOS;

            PlayerSettings.SetApplicationIdentifier(iOS, BundleIdentifier);
            PlayerSettings.SetScriptingBackend(iOS, ScriptingImplementation.IL2CPP);
            PlayerSettings.SetIl2CppCompilerConfiguration(iOS, Il2CppCompilerConfiguration.Release);
            PlayerSettings.SetManagedStrippingLevel(iOS, ManagedStrippingLevel.Minimal);

            PlayerSettings.iOS.sdkVersion = iOSSdkVersion.DeviceSDK;
            PlayerSettings.iOS.targetOSVersionString = k_MinimumOSVersion;
            PlayerSettings.iOS.appleEnableAutomaticSigning = true;
            PlayerSettings.iOS.appleDeveloperTeamID =
                Environment.GetEnvironmentVariable("APPLE_TEAM_ID") ?? PlayerSettings.iOS.appleDeveloperTeamID;

            // El juego es landscape puro (joystick y camara asumen esa orientacion).
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;
            PlayerSettings.useAnimatedAutorotation = true;

            // El master server se habla por HTTP plano (http://IP:8000), asi que hay que
            // dejar pasar HTTP inseguro tambien del lado de UnityWebRequest. La excepcion
            // equivalente de App Transport Security la agrega IOSPostProcess.
            PlayerSettings.insecureHttpOption = InsecureHttpOption.AlwaysAllowed;

            AssetDatabase.SaveAssets();
        }
    }
}
