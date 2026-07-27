using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace Unity.BossRoom.Gameplay.UserInput
{
    /// <summary>
    /// TEMPORARY diagnostic for "the UI renders but doesn't respond in the gameplay scene".
    /// Delete this file once the cause is known.
    ///
    /// On every left click / touch press (and on F9) it dumps what the EventSystem sees: how many
    /// EventSystems are alive and which one is current, whether its input module is enabled, and the
    /// full ordered list of what a UI raycast at that point hits. The topmost hit is whatever is
    /// receiving the click; an empty list means nothing raycastable is under the pointer at all.
    /// </summary>
    public class UIClickDiagnostics : MonoBehaviour
    {
        readonly List<RaycastResult> m_Results = new List<RaycastResult>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            var go = new GameObject("UIClickDiagnostics");
            DontDestroyOnLoad(go);
            go.AddComponent<UIClickDiagnostics>();
        }

        void Update()
        {
            var mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame)
            {
                Dump("mouse left", mouse.position.ReadValue());
                return;
            }

            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.f9Key.wasPressedThisFrame)
            {
                Dump("F9", mouse != null ? mouse.position.ReadValue() : new Vector2(Screen.width * 0.5f, Screen.height * 0.5f));
                return;
            }

            var touchscreen = Touchscreen.current;
            if (touchscreen == null)
            {
                return;
            }

            foreach (var touch in touchscreen.touches)
            {
                if (touch.press.wasPressedThisFrame)
                {
                    Dump($"touch {touch.touchId.ReadValue()}", touch.position.ReadValue());
                    return;
                }
            }
        }

        void Dump(string source, Vector2 screenPos)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"[UIDiag] {source} at {screenPos}  (screen {Screen.width}x{Screen.height}, timeScale {Time.timeScale})");

            var systems = FindObjectsByType<EventSystem>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            sb.AppendLine($"[UIDiag] EventSystems alive: {systems.Length}   current: {(EventSystem.current != null ? EventSystem.current.name : "<null>")}");

            foreach (var es in systems)
            {
                var module = es.currentInputModule;
                sb.AppendLine($"[UIDiag]   - '{es.name}' scene='{es.gameObject.scene.name}' activeInHierarchy={es.gameObject.activeInHierarchy} " +
                              $"componentEnabled={es.enabled} isCurrent={EventSystem.current == es} " +
                              $"module={(module != null ? module.GetType().Name : "<none>")} moduleEnabled={(module != null && module.enabled)}");
            }

            if (EventSystem.current == null)
            {
                Debug.Log(sb.ToString());
                return;
            }

            sb.AppendLine($"[UIDiag] IsPointerOverGameObject: {EventSystem.current.IsPointerOverGameObject()}");

            var pointerData = new PointerEventData(EventSystem.current) { position = screenPos };
            m_Results.Clear();
            EventSystem.current.RaycastAll(pointerData, m_Results);

            if (m_Results.Count == 0)
            {
                sb.AppendLine("[UIDiag] raycast hits: NONE (no raycastable graphic under the pointer)");
            }
            else
            {
                sb.AppendLine($"[UIDiag] raycast hits (topmost first), {m_Results.Count}:");
                for (int i = 0; i < m_Results.Count; i++)
                {
                    var hit = m_Results[i];
                    var canvas = hit.gameObject != null ? hit.gameObject.GetComponentInParent<Canvas>() : null;
                    var rootCanvas = canvas != null ? canvas.rootCanvas : null;
                    sb.AppendLine($"[UIDiag]   {i}: '{Path(hit.gameObject)}' " +
                                  $"canvas='{(rootCanvas != null ? rootCanvas.name : "?")}' " +
                                  $"sortingOrder={(rootCanvas != null ? rootCanvas.sortingOrder : 0)} " +
                                  $"renderMode={(rootCanvas != null ? rootCanvas.renderMode.ToString() : "?")} " +
                                  $"module={hit.module?.GetType().Name}");
                }
            }

            Debug.Log(sb.ToString());
        }

        static string Path(GameObject go)
        {
            if (go == null)
            {
                return "<null>";
            }

            var sb = new StringBuilder(go.name);
            var t = go.transform.parent;
            while (t != null)
            {
                sb.Insert(0, t.name + "/");
                t = t.parent;
            }
            return sb.ToString();
        }
    }
}
