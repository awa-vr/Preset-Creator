using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using VRC.SDKBase;

namespace AwAVR.PresetCreator
{
    class Preset
    {
        public VRC_AvatarParameterDriver.Parameter Parameter { get; set; }
        public bool Change { get; set; } // If the parameter should change in the preset
        public bool Modified { get; set; } // If the parameter has changed options that are not yet applied

        public Preset(VRC_AvatarParameterDriver.Parameter parameter, bool change)
        {
            Parameter = parameter;
            Change = change;
            Modified = false;
        }
    }

    public class PresetCreator : EditorWindow
    {
        #region Variables

        private static string _windowTitle = "Preset Creator";
        private List<VRCAvatarDescriptor> _avatars = null;
        private VRCAvatarDescriptor _avatar = null;
        private VRCExpressionParameters _vrcParams = null;
        private AnimatorController _fx = null;
        private AnimatorControllerLayer _presetsLayer = null;
        private AnimatorState _currentPreset = null;
        private ChildAnimatorState[] _presets = { };
        private List<Preset> _presetsParameters = new List<Preset>();
        private string _newPresetName = "";
        private bool _addNewPreset = false;
        private bool _renamePreset = false;
        private bool _writeDefaults = false;
        private Vector2 _scrollPos = Vector2.zero;
        private int _selectedPresetIndex = 0;
        private int _lastSelectedPresetIndex = -1;

        // Settings
        private static bool _hasReadMenuWarning = false;
        private const string HasReadMenuWarningKey = "HasReadMenuWarning";

        #endregion

        #region Window

        [MenuItem("Tools/AwA/Preset Creator", false, -100)]
        public static void ShowWindow()
        {
            var window = GetWindow<PresetCreator>(_windowTitle);
            window.titleContent = new GUIContent(
                image: EditorGUIUtility.IconContent("d_Audio Mixer@2x").image,
                text: _windowTitle,
                tooltip: "Create preset for a VRChat avatar"
            );
            window.minSize = new Vector2(450f, window.minSize.y);

            LoadSettings();
        }

        public void OnEnable()
        {
            _avatars = Core.GetAvatarsInScene();

            if (_avatars.Count == 0)
            {
                EditorGUILayout.HelpBox("Please place an avatar in the scene", MessageType.Error);
                _avatars = null;
                return;
            }

            if (_avatars.Count == 1)
            {
                _avatar = _avatars.First();
                _avatars.Clear();
                return;
            }

            LoadSettings();
        }

        private static void LoadSettings()
        {
            if (EditorPrefs.HasKey(HasReadMenuWarningKey))
                _hasReadMenuWarning = EditorPrefs.GetBool(HasReadMenuWarningKey);
        }

        public void OnGUI()
        {
            Core.Title(_windowTitle);

            // Info box
            if (!_hasReadMenuWarning)
            {
                EditorGUILayout.HelpBox(
                    "This tool won't automatically add the preset to a menu, you will have to do this manually." +
                    "\n\n" +
                    "More info can be found here: https://awa-vr.github.io/vrc-docs/docs/unity/presets/#menu\n" +
                    "(Click the button below to open)",
                    MessageType.Warning);
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Open Link"))
                        Application.OpenURL("https://awa-vr.github.io/vrc-docs/docs/unity/presets/#menu");

                    if (GUILayout.Button("Close Warning"))
                    {
                        EditorPrefs.SetBool(HasReadMenuWarningKey, true);
                        _hasReadMenuWarning = true;
                    }
                }

                GUILayout.Space(10);
            }

            // Avatar
            Core.GetAvatar(ref _avatar, ref _avatars);
            if (!_avatar)
            {
                EditorGUILayout.HelpBox("Please select an avatar", MessageType.Error);
                return;
            }

            // Get avatar parameters
            if (_avatar.expressionParameters)
                _vrcParams = _avatar.expressionParameters;
            else
                return;

            // Get FX Animator
            _fx = Core.GetAnimatorController(_avatar);

            if (_fx == null)
            {
                EditorGUILayout.HelpBox("Can't find an FX animator on your avatar. Please add one", MessageType.Error);
                return;
            }

            // Presets layer
            _presetsLayer = Core.GetLayerByName(_fx, "Presets");
            if (_presetsLayer == null)
            {
                ShowWriteDefaultsToggle();
                AskAddPresetsLayer();
                return;
            }

            // Get presets
            _presets = _presetsLayer.stateMachine.states;
            if (_presets.Length == 1)
            {
                EditorGUILayout.HelpBox("No presets found.", MessageType.Info);
                AddPreset();
                return;
            }

            // Preset selection
            PresetSelection();

            // New preset
            if (_addNewPreset)
            {
                AddPreset();
                if (GUILayout.Button("Cancel"))
                {
                    _addNewPreset = false;
                    UpdatePresetsList();
                }

                return;
            }

            // New/Update preset buttons
            ShowEditButtons();

            // Rename Preset Shiz
            if (_renamePreset)
            {
                ShowRenameGUI();
                return;
            }

            // List params
            if (_currentPreset)
            {
                // Header
                GUILayout.BeginHorizontal();
                GUILayout.Label("Parameter", GUILayout.ExpandWidth(true));
                GUILayout.Label("Type", GUILayout.Width(60));
                GUILayout.Label("Value", GUILayout.Width(60));
                GUILayout.Label("Change", GUILayout.Width(60));
                GUILayout.EndHorizontal();

                // Items
                _scrollPos = GUILayout.BeginScrollView(_scrollPos);
                ListParams();
                GUILayout.EndScrollView();
            }
        }

        #endregion

        #region GUIHelpers

        private void ShowWriteDefaultsToggle()
        {
            var content = new GUIContent("Write Defaults",
                tooltip: "This just exists to shut up the VRChat SDK and VRCFury.");
            _writeDefaults = GUILayout.Toggle(_writeDefaults, content, GUILayout.Width(100));
        }

        private void PresetSelection()
        {
            using (new GUILayout.HorizontalScope())
            {
                var presetNames = _presets.Select(p => p.state.name).Where(name => name != "Idle").ToArray();
                _selectedPresetIndex = EditorGUILayout.Popup("Preset", _selectedPresetIndex, presetNames);

                if (_selectedPresetIndex != _lastSelectedPresetIndex)
                {
                    // Check if there are unsaved changes
                    if (_presetsParameters.Any(p => p.Modified))
                    {
                        if (!EditorUtility.DisplayDialog("Unsaved Changes",
                                "You have unsaved changes in the current preset. Do you want to continue without saving?",
                                "Discard Changes", "Cancel"))
                        {
                            _selectedPresetIndex = _lastSelectedPresetIndex;
                            return;
                        }
                    }

                    UpdatePresetsList();
                }

                ShowWriteDefaultsToggle();
            }
        }

        private void ShowEditButtons()
        {
            using (new GUILayout.HorizontalScope())
            {
                var style = new GUIStyle(GUI.skin.button);
                // style.imagePosition = ImagePosition.ImageOnly;

                if (GUILayout.Button(new GUIContent("New", EditorGUIUtility.IconContent("d_Toolbar Plus").image),
                        style))
                    NewPreset();

                if (GUILayout.Button(new GUIContent("Delete", EditorGUIUtility.IconContent("d_Toolbar Minus").image),
                        style))
                    DeletePreset();

                if (GUILayout.Button(new GUIContent("Rename", EditorGUIUtility.IconContent("d_editicon.sml").image),
                        style))
                    RenamePreset();

                if (GUILayout.Button(new GUIContent("Update", EditorGUIUtility.IconContent("Refresh").image), style))
                    UpdatePreset();
            }
        }

        private void ShowRenameGUI()
        {
#if PARAMETER_RENAMER_V1_1_0
            using (new GUILayout.VerticalScope())
            {
                var presetName = GetPresetName();
                using (new EditorGUI.DisabledGroupScope(true))
                {
                    EditorGUILayout.TextField("Old Name", presetName);
                }

                _newPresetName = EditorGUILayout.TextField("New Name", _newPresetName);

                if (GUILayout.Button("Rename"))
                {
                    if (EditorUtility.DisplayDialog("Confirm",
                            $"Are you sure you want to rename preset \"{presetName}\" to \"{_newPresetName}\"", "Yes",
                            "No")) ;
                    {
                        var parameterName = $"Presets/{_newPresetName}";

                        // Check if preset exists
                        bool paramExists = false;
                        foreach (var vrcParam in _vrcParams.parameters)
                        {
                            if (vrcParam.name == parameterName)
                            {
                                paramExists = true;
                                break;
                            }
                        }

                        foreach (var fxParam in _fx.parameters)
                        {
                            if (fxParam.name == parameterName)
                            {
                                paramExists = true;
                                break;
                            }
                        }

                        // Renaming SHiz
                        if (paramExists)
                        {
                            EditorUtility.DisplayDialog("Name already used", "New preset name is already used.", "OK");
                        }
                        else
                        {
                            Undo.RecordObject(_fx, "Rename Preset");
                            _presetsLayer.stateMachine.states[_selectedPresetIndex + 1].state.name = _newPresetName;
                            Core.Cleany(_fx);

                            ParameterRenamer.Show($"Presets/{presetName}", _avatar, $"Presets/{_newPresetName}", true);
                        }

                        _renamePreset = false;
                    }
                }
            }
#else
            EditorGUILayout.HelpBox("Parameter Renamer not installed.", MessageType.Error);
#endif
        }

        #endregion

        #region Methods

        private void UpdatePresetsList()
        {
            _currentPreset = _presets[_selectedPresetIndex + 1].state;
            _writeDefaults = _currentPreset.writeDefaultValues;
            var parameterDriver = _currentPreset.behaviours.OfType<VRCAvatarParameterDriver>().First();

            _presetsParameters.Clear();
            foreach (var vrcParamsParameter in _vrcParams.parameters)
            {
                var value = 0f;
                var isPreset = false;

                var presetParam =
                    parameterDriver.parameters.FirstOrDefault(p => p.name == vrcParamsParameter.name);
                if (presetParam != null)
                {
                    value = presetParam.value;
                    isPreset = true;
                }

                var presetParameter = new Preset(new VRC_AvatarParameterDriver.Parameter
                    {
                        name = vrcParamsParameter.name,
                        value = value
                    },
                    isPreset
                );
                _presetsParameters.Add(presetParameter);

                _lastSelectedPresetIndex = _selectedPresetIndex;
            }
        }

        private void AskAddPresetsLayer()
        {
            EditorGUILayout.HelpBox("No presets layer found!", MessageType.Error);

            GUILayout.Label("Create one now?");
            if (GUILayout.Button("Create Presets Layer"))
            {
                AnimatorControllerLayer presetsLayer = new AnimatorControllerLayer
                {
                    name = "AwA - Presets - DO NOT TOUCH",
                    defaultWeight = 1f,
                    stateMachine = new AnimatorStateMachine
                        { name = "AwA - Presets - DO NOT TOUCH", hideFlags = HideFlags.HideInHierarchy }
                };
                presetsLayer.stateMachine.exitPosition = new Vector3(50f, 70f, 0f);

                // Add layer
                Undo.RecordObject(_fx, "Add Presets Layer");
                AssetDatabase.AddObjectToAsset(presetsLayer.stateMachine, AssetDatabase.GetAssetPath(_fx));
                _fx.AddLayer(presetsLayer);

                // add empty clip
                var emptyClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(Path.Combine(
                    Path.GetDirectoryName(AssetDatabase.GetAssetPath(MonoScript.FromScriptableObject(this))),
                    "Empty Clip.anim"));
                if (emptyClip == null)
                {
                    Debug.LogError("Can't find 'Empty Clip' animation!");
                    return;
                }

                var emptyState = presetsLayer.stateMachine.AddState("Idle");
                emptyState.motion = emptyClip;

                Core.Cleany(_fx);
            }
        }

        private void AddPreset()
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Preset Name", GUILayout.Width(100));
            _newPresetName = EditorGUILayout.TextField(_newPresetName);
            EditorGUILayout.EndHorizontal();

            if (GUILayout.Button("Add Preset", GUILayout.ExpandWidth(true)))
            {
                var emptyClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(Path.Combine(
                    Path.GetDirectoryName(AssetDatabase.GetAssetPath(MonoScript.FromScriptableObject(this))),
                    "Empty Clip.anim"));
                if (emptyClip == null)
                {
                    Debug.LogError("Can't find 'Empty Clip' animation!");
                    return;
                }

                // Undo
                Undo.RecordObject(_fx, $"Add Preset: {_newPresetName}");

                // add animation state
                var newPreset = _presetsLayer.stateMachine.AddState(_newPresetName);
                newPreset.motion = emptyClip;
                newPreset.writeDefaultValues = _writeDefaults;
                newPreset.AddStateMachineBehaviour<VRCAvatarParameterDriver>();

                // add parameters to vrc params
                string paramName = "Presets/" + _newPresetName;
                bool paramExists = false;

                foreach (var vrcParam in _vrcParams.parameters)
                {
                    if (vrcParam.name == _newPresetName)
                    {
                        paramExists = true;
                        break;
                    }
                }

                if (!paramExists)
                {
                    var newVRCParam = new VRCExpressionParameters.Parameter
                    {
                        name = paramName,
                        valueType = VRCExpressionParameters.ValueType.Bool,
                        defaultValue = 0.0f,
                        saved = false,
                        networkSynced = false
                    };

                    List<VRCExpressionParameters.Parameter> newParamsList =
                        new List<VRCExpressionParameters.Parameter>();

                    foreach (var param in _vrcParams.parameters)
                    {
                        newParamsList.Add(param);
                    }

                    newParamsList.Add(newVRCParam);
                    _vrcParams.parameters = newParamsList.ToArray();

                    Core.Cleany(_vrcParams);
                }

                // add parameter to fx params
                paramExists = false;
                foreach (var fxParam in _fx.parameters)
                {
                    if (fxParam.name == paramName)
                    {
                        paramExists = true;
                        break;
                    }
                }

                if (!paramExists)
                {
                    var newFXParam = new AnimatorControllerParameter();
                    newFXParam.type = AnimatorControllerParameterType.Bool;
                    newFXParam.name = paramName;
                    newFXParam.defaultBool = false;

                    _fx.AddParameter(newFXParam);
                }

                // Create transitions
                var idleState = _presetsLayer.stateMachine.states
                    .FirstOrDefault(state => state.state.name == "Idle")
                    .state;
                if (idleState == null)
                {
                    Debug.LogError("Can't find 'Idle' state!");
                    Core.Cleany(_fx);
                    return;
                }

                var newPresetState = _presetsLayer.stateMachine.states
                    .FirstOrDefault(state => state.state.name == _newPresetName).state;
                if (newPresetState == null)
                {
                    Debug.LogError("Can't find '" + _newPresetName + "' state!");
                    Core.Cleany(_fx);
                    return;
                }

                var idleStateTransition = idleState.AddTransition(newPresetState);
                idleStateTransition.conditions = new[]
                {
                    new AnimatorCondition
                    {
                        mode = AnimatorConditionMode.If,
                        parameter = paramName
                    }
                };
                idleStateTransition.exitTime = 0f;
                idleStateTransition.hasExitTime = false;
                idleStateTransition.hasFixedDuration = true;
                idleStateTransition.duration = 0f;
                idleStateTransition.offset = 0f;
                idleStateTransition.interruptionSource = TransitionInterruptionSource.None;

                var newPresetTransition = newPresetState.AddExitTransition();
                newPresetTransition.conditions = new[]
                {
                    new AnimatorCondition
                    {
                        mode = AnimatorConditionMode.IfNot,
                        parameter = paramName
                    }
                };
                newPresetTransition.exitTime = 0f;
                newPresetTransition.hasExitTime = false;
                newPresetTransition.hasFixedDuration = true;
                newPresetTransition.duration = 0f;
                newPresetTransition.offset = 0f;
                newPresetTransition.interruptionSource = TransitionInterruptionSource.None;

                Core.Cleany(_fx);
                _newPresetName = "";
                _addNewPreset = false;
                _currentPreset = newPresetState;
            }
        }

        private void ListParams()
        {
            if (_currentPreset == null)
                return;

            foreach (var param in _vrcParams.parameters)
            {
                if (param.name.StartsWith("Presets/") || param.name.StartsWith("Blend/"))
                    continue;

                var preset = _presetsParameters.FirstOrDefault(p => p.Parameter.name == param.name);
                if (preset == null)
                    continue;

                var isInPreset = preset.Change;

                GUILayout.BeginHorizontal(EditorStyles.helpBox);
                GUILayout.Label(param.name, GUILayout.ExpandWidth(true));

                EditorGUI.BeginDisabledGroup(!isInPreset);
                GUI.color = preset.Modified ? Color.yellow : Color.white;
                switch (param.valueType)
                {
                    case VRCExpressionParameters.ValueType.Bool:
                        GUILayout.Label("bool", EditorStyles.boldLabel, GUILayout.Width(45));

                        bool newBoolValue = EditorGUILayout.Toggle(
                            isInPreset ? Convert.ToBoolean(preset.Parameter.value) : false,
                            GUILayout.Width(60)
                        );
                        if (preset.Parameter.value != Convert.ToSingle(newBoolValue))
                        {
                            preset.Parameter.value = Convert.ToSingle(newBoolValue);
                            preset.Modified = true;
                        }

                        break;
                    case VRCExpressionParameters.ValueType.Int:
                        GUILayout.Label("int", EditorStyles.boldLabel, GUILayout.Width(45));

                        int newIntValue = EditorGUILayout.IntField((int)preset.Parameter.value, GUILayout.Width(60));
                        if (preset.Parameter.value != newIntValue)
                        {
                            preset.Parameter.value = newIntValue;
                            preset.Modified = true;
                        }

                        break;
                    case VRCExpressionParameters.ValueType.Float:
                        GUILayout.Label("float", EditorStyles.boldLabel, GUILayout.Width(45));

                        float newFloatValue = EditorGUILayout.FloatField(preset.Parameter.value, GUILayout.Width(60));
                        if (preset.Parameter.value != newFloatValue)
                        {
                            preset.Parameter.value = newFloatValue;
                            preset.Modified = true;
                        }

                        break;
                    default:
                        EditorGUILayout.TextArea(isInPreset ? preset.Parameter.value.ToString() : "0",
                            GUILayout.Width(60));
                        break;
                }

                EditorGUI.EndDisabledGroup();

                // Change button
                GUILayout.BeginHorizontal(GUILayout.Width(60));
                GUILayout.Space(30);
                bool newChangeValue = EditorGUILayout.Toggle(isInPreset);
                GUILayout.EndHorizontal();
                if (preset.Change != newChangeValue)
                {
                    preset.Change = newChangeValue;
                    preset.Modified = true;
                }

                GUI.color = Color.white;
                GUILayout.EndHorizontal();
            }
        }

        private void NewPreset()
        {
            Debug.Log("New Preset");
            _addNewPreset = true;
        }

        private void UpdatePreset()
        {
            if (!_currentPreset)
                return;

            Debug.Log("Update Preset: " + _currentPreset.name);
            var parameterDriver = _currentPreset.behaviours.OfType<VRCAvatarParameterDriver>().First();

            Undo.RecordObject(parameterDriver, $"Update Preset: {_currentPreset.name}");
            parameterDriver.parameters.Clear();

            foreach (var preset in _presetsParameters)
            {
                // Skip if the parameter shouldn't be changed by the preset
                if (!preset.Change)
                    continue;

                parameterDriver.parameters.Add(preset.Parameter);
            }

            parameterDriver.parameters.Add(new VRC_AvatarParameterDriver.Parameter
            {
                name = "Presets/" + _currentPreset.name,
                value = 0f
            });

            Core.Cleany(parameterDriver);
            UpdatePresetsList();
        }

        private void RenamePreset()
        {
#if PARAMETER_RENAMER_V1_1_0
            _renamePreset = true;
#else
            EditorUtility.DisplayDialog("Install Parameter Renamer",
                "To rename a preset you need to install Parameter Renamer v1.1.0 or later from VCC.",
                "OK");
#endif
        }

        private void DeletePreset()
        {
            var presetName = GetPresetName();
            if (EditorUtility.DisplayDialog("Confirm",
                    $"Are you sure you want to delete preset \"{presetName}\"?\nThis will not remove it from any menus you made have had the preset added to.",
                    "Yes",
                    "No"))
            {
                var objects = new UnityEngine.Object[] { _fx, _vrcParams };
                Undo.RecordObjects(objects, $"Delete Preset: {presetName}");

                var parameterName = $"Presets/{presetName}";

                // Remove state from Presets Layer
                var presetState = _presetsLayer.stateMachine.states
                    .FirstOrDefault(state => state.state.name == parameterName).state;
                _presetsLayer.stateMachine.RemoveState(presetState);

                // Remove from fx parameters
                var parameterIndex = _fx.parameters.ToList().FindIndex(p => p.name == parameterName);
                _fx.RemoveParameter(parameterIndex);

                // Remove from VRC parameters
                var newParamsList = new List<VRCExpressionParameters.Parameter>();
                foreach (var param in _vrcParams.parameters)
                {
                    if (param.name != parameterName)
                        newParamsList.Add(param);
                }

                _vrcParams.parameters = newParamsList.ToArray();

                Core.CleanObjects(objects);
            }
        }

        private string GetPresetName()
        {
            return _presets[_selectedPresetIndex + 1].state.name;
        }

        #endregion
    }
}