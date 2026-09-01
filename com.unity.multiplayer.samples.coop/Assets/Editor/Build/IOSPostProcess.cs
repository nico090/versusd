#if UNITY_IOS
using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;
using UnityEngine;

namespace VersusD.EditorTools
{
    /// <summary>
    /// Retoca el proyecto Xcode que genera Unity. Sin esto el build compila igual pero en
    /// el iPhone no conecta: App Transport Security bloquea el HTTP plano del master server.
    /// </summary>
    public static class IOSPostProcess
    {
        [PostProcessBuild(999)]
        public static void OnPostProcessBuild(BuildTarget target, string pathToBuiltProject)
        {
            if (target != BuildTarget.iOS)
            {
                return;
            }

            PatchInfoPlist(pathToBuiltProject);
            PatchXcodeProject(pathToBuiltProject);
        }

        static void PatchInfoPlist(string pathToBuiltProject)
        {
            var plistPath = Path.Combine(pathToBuiltProject, "Info.plist");
            var plist = new PlistDocument();
            plist.ReadFromFile(plistPath);
            var root = plist.root;

            // ATS: el master server (http://IP:8001) y el endpoint del relay son HTTP plano
            // sobre IP, sin certificado. iOS los tira abajo salvo que se declare la excepcion.
            // OJO: no agregar aca NSAllowsLocalNetworking ni las otras claves granulares.
            // Si alguna de ellas esta presente, iOS 10+ ignora NSAllowsArbitraryLoads y solo
            // aplica la regla fina, con lo cual el HTTP plano contra una IP publica se bloquea.
            var ats = root.CreateDict("NSAppTransportSecurity");
            ats.SetBoolean("NSAllowsArbitraryLoads", true);

            // Evita el cuestionario de export compliance en cada subida a TestFlight.
            root.SetBoolean("ITSAppUsesNonExemptEncryption", false);

            // El juego es landscape; sin esto iOS puede ofrecer multitasking en iPad y
            // reescalar la UI a tamaños que el HUD no contempla.
            root.SetBoolean("UIRequiresFullScreen", true);

            plist.WriteToFile(plistPath);
            Debug.Log("[IOSPostProcess] Info.plist parcheado (ATS + export compliance + fullscreen).");
        }

        static void PatchXcodeProject(string pathToBuiltProject)
        {
            var projectPath = PBXProject.GetPBXProjectPath(pathToBuiltProject);
            var project = new PBXProject();
            project.ReadFromFile(projectPath);

            var unityFramework = project.GetUnityFrameworkTargetGuid();
            var mainTarget = project.GetUnityMainTargetGuid();

            foreach (var guid in new[] { unityFramework, mainTarget })
            {
                // Los sockets UDP de Mirror/LRM no necesitan bitcode, y Xcode 14+ lo removio.
                project.SetBuildProperty(guid, "ENABLE_BITCODE", "NO");
            }

            // UnityFramework va embebido dentro del .app, y iOS rechaza la instalacion si un
            // sub-bundle comparte identificador con el padre (MIInstallerErrorDomain 57,
            // DuplicateIdentifier). Es facil de romper a mano: al elegir el team en la pestaña
            // Signing & Capabilities de este target, es habitual editarle tambien el bundle id y
            // dejarle el de la app. Se reescribe en cada build para que no dependa de eso.
            project.SetBuildProperty(unityFramework, "PRODUCT_BUNDLE_IDENTIFIER",
                IOSBuild.BundleIdentifier + ".framework");

            // Solo arm64: los dispositivos con armv7 no llegan al minimo de iOS que pedimos.
            project.SetBuildProperty(mainTarget, "ARCHS", "arm64");

            project.WriteToFile(projectPath);
            Debug.Log("[IOSPostProcess] Proyecto Xcode parcheado (bitcode off, arm64).");
        }
    }
}
#endif
