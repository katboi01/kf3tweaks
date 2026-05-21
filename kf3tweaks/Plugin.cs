using BepInEx;
using UnityEngine;
using System.Reflection;
using System;
using UnityEngine.UI;
using System.Collections.Generic;
using static team.pinewood.utilities.Reflection;
using Battle;

namespace kf3tweaks
{
    [BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
    [BepInProcess("けもフレ３.exe")]
    public class Plugin : BaseUnityPlugin
    {
        public static int RENDER_TEXTURE_RESOLUTION_MULT = 2; // Multiplier for texture sizes used with camera render-to-texture. The game usually uses 1024x for these

        // Keys for issuing commands to friends in combat
        // index 0 = friend 1 (from left to right)
        public static KeyCode[] FRIEND_ACT_KEYCODES = new KeyCode[]
        {
            KeyCode.Alpha1,
            KeyCode.Alpha2,
            KeyCode.Alpha3,
            KeyCode.Alpha4,
            KeyCode.Alpha5,
        };

        private bool showOverlay = false;

        private void Awake()
        {
            // Plugin startup logic
            Logger.LogInfo($"Plugin {PluginInfo.PLUGIN_GUID} is loaded!");

            On.CanvasManager.Initialize += CanvasManager_Initialize;
            On.RenderTextureChara.SetupRenderTexture += RenderTextureChara_SetupRenderTexture;
            On.UserOptionData.SetDisplayQuality += UserOptionData_SetDisplayQuality;
            On.SceneManager.InitializeOption += SceneManager_InitializeOption;
            On.SceneBattle.Update += SceneBattle_Update;
            On.SceneHome.OnEnableScene += SceneHome_OnEnableScene;
            On.SceneHome.Update += SceneHome_Update;
        }

        private void SceneManager_InitializeOption(On.SceneManager.orig_InitializeOption orig)
        {
            orig();
            SceneManager.screenSize = new Resolution() { width = 1600, height = 900 };
            Logger.LogInfo("Patched screensize");
        }

        // Increase AA quality and enable aniso filtering
        // Large increase in graphics fidelity
        private void UserOptionData_SetDisplayQuality(On.UserOptionData.orig_SetDisplayQuality orig, UserOptionData self)
        {
            orig(self); // Not really necessary, on PC this method does nothing else

            QualitySettings.antiAliasing = 8; // Default is 1 in low-quality mode, 2 in high-quality
            QualitySettings.anisotropicFiltering = AnisotropicFiltering.Enable;
        }

        // Increase resolution of textures used with camera render-to-texture
        // Affects friend growth screen (super blurry normally!), newcomer gacha screen, daily login assistant, friend details, possibly more
        private void RenderTextureChara_SetupRenderTexture(On.RenderTextureChara.orig_SetupRenderTexture orig, RenderTextureChara self, int w, int h)
        {
            // Call orig with increased texture size
            orig(self, w * RENDER_TEXTURE_RESOLUTION_MULT, h * RENDER_TEXTURE_RESOLUTION_MULT);

            // SetupRenderTexture() is in some places called twice - once explicitly, another time from Start() - we need to not apply the size multiplier twice
            SetField<RenderTextureChara>(self, "width", w);
            SetField<RenderTextureChara>(self, "height", h);

            // Set size of the UI element back to the intended one - necessary as the end of SetupRenderTexture() calls SetNativeSize()
            FieldInfo field = typeof(RenderTextureChara).GetField("m_dispTexture", BindingFlags.NonPublic | BindingFlags.Instance);
            RawImage dispTexture = field.GetValue(self) as RawImage;
            RectTransform rect = dispTexture.transform as RectTransform;
            rect.sizeDelta = new Vector2(w, h);
        }

        private void Update()
        {
            // Poll keybinds
            if (Input.GetKeyDown(KeyCode.RightControl)) // Toggle FPS cap
            {
                bool isUncapped = QualitySettings.vSyncCount != 0;
                QualitySettings.vSyncCount = isUncapped ? 0 : 1;
            }
            if (Input.GetKey(KeyCode.RightAlt) && Input.GetKeyDown(KeyCode.Return)) // Toggle borderless windowed
            {
                Resolution bestRes = Screen.resolutions[Screen.resolutions.Length - 1];
                Screen.SetResolution(bestRes.width, bestRes.height, Screen.fullScreenMode == FullScreenMode.FullScreenWindow ? FullScreenMode.Windowed : FullScreenMode.FullScreenWindow);
            }

            showOverlay = Input.GetKey(KeyCode.F1);
        }

        private void OnGUI()
        {
            if (showOverlay)
            {
                // Display JST time
                DateTime jst = DateTime.UtcNow.AddHours(9);
                GUI.Box(new Rect(0, 0, 100, 25), new GUIContent(jst.ToString("HH:mm") + " JST"));
            }
        }

        // Disable resolution lock
        private void CanvasManager_Initialize(On.CanvasManager.orig_Initialize orig)
        {
            Type type = typeof(CanvasManager);
            FieldInfo info = type.GetField("oldWndProc", BindingFlags.NonPublic | BindingFlags.Static);

            // Prevents SetWindowProc from running, since the check in Update() is != zero. This field is not used for anything else, so it needn't be any valid pointer.
            info.SetValue(null, IntPtr.Zero + 1);

            orig();
        }

        private void SceneBattle_Update(On.SceneBattle.orig_Update orig, SceneBattle self)
        {
            // Check keybinds
            try
            {
                for (int i = 0; i < FRIEND_ACT_KEYCODES.Length; ++i)
                {
                    KeyCode keyCode = FRIEND_ACT_KEYCODES[i];
                    if (Input.GetKeyDown(keyCode))
                    {
                        SceneBattle_GUI gui = GetField<SceneBattle, SceneBattle_GUI>(self, "_guiData", BindingFlags.NonPublic | BindingFlags.Instance);
                        List<Transform> characterClickboxes; 
                        if (Input.GetKey(KeyCode.LeftShift)) // Touch miracles if lshift is held
                        {
                            characterClickboxes = gui.TouchArts;
                        }
                        else if (Input.GetKey(KeyCode.LeftControl)) // Touch standby skill if lctrl is held
                        {
                            characterClickboxes = gui.TouchWait;
                        }
                        else // Otherwise touch character
                        {
                            characterClickboxes = gui.TouchChara;
                        }

                        if (i < characterClickboxes.Count)
                        {
                            SetField<SceneBattle>(self, "_currentTouch", characterClickboxes[i]);
                        }
                    }
                }
                if (Input.GetKeyDown(KeyCode.Escape)) // Cancel flag order
                {
                    SetField<SceneBattle>(self, "cancelCardBtn", true);
                }
                if (Input.GetKeyDown(KeyCode.Space)) // Use refill
                {
                    SceneBattle_GUI gui = GetField<SceneBattle, SceneBattle_GUI>(self, "_guiData", BindingFlags.NonPublic | BindingFlags.Instance);
                    SetField<SceneBattle>(self, "_currentTouch", gui.TouchActGage);
                }
                if (Input.GetKeyDown(KeyCode.F)) // Toggle fast mode
                {
                    SceneBattle_GUI gui = GetField<SceneBattle, SceneBattle_GUI>(self, "_guiData", BindingFlags.NonPublic | BindingFlags.Instance);
                    gui.BtnFast.m_Button.onClick?.Invoke();
                }
                if (Input.GetKeyDown(KeyCode.A)) // Toggle autoplay
                {
                    SceneBattle_GUI gui = GetField<SceneBattle, SceneBattle_GUI>(self, "_guiData", BindingFlags.NonPublic | BindingFlags.Instance);
                    gui.BtnAuto.m_Button.onClick?.Invoke();
                }
            }
            catch (Exception e)
            {
                Logger.LogError(e);
            }

            orig(self);
        }

        private void SceneHome_OnEnableScene(On.SceneHome.orig_OnEnableScene orig, SceneHome self, object args)
        {
            orig(self, args);
            UnrestrictHomeCamera(self);
        }

        private void SceneHome_Update(On.SceneHome.orig_Update orig, SceneHome self)
        {
            bool viewPositionChanged = GetField<SceneHome, bool>(self, "viewChg");

            orig(self); // Polls viewChg, set from change position button click

            if (viewPositionChanged)
            {
                UnrestrictHomeCamera(self);
            }
        }

        private void UnrestrictHomeCamera(SceneHome scene)
        {
            SetField<SceneHome>(scene, "viewMin", new Vector3(-180, -180, -180));
            SetField<SceneHome>(scene, "viewMax", new Vector3(180, 180, 180));
        }
    }
}
