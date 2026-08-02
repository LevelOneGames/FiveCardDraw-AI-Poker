using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>
/// 홈 화면에서 AI 플레이어 1~4의 성향 토글과 세부 슬라이더를 관리합니다.
///
/// 성향 토글: 보수형, 공격형, 계산형, 변칙형
/// 세부 슬라이더: 공격성, 패 선별력, 블러프 성향
///
/// 성향 토글을 켜면 해당 프리셋 값이 세 슬라이더에 자동 적용됩니다.
/// 슬라이더를 하나라도 직접 변경하면 모든 성향 토글이 꺼지고
/// PokerAIStyle.Custom으로 PlayerControl에 적용됩니다.
/// </summary>
public class HomeAISettingsController : MonoBehaviour
{
    [Serializable]
    public class PlayerAISettingUI
    {
        [Tooltip("자동으로 1~4가 지정됩니다.")]
        [Range(1, 4)]
        public int playerNumber = 1;

        [Header("Style Toggles")]
        [Tooltip("보수형 성향 토글입니다.")]
        public Toggle conservativeToggle;

        [Tooltip("공격형 성향 토글입니다.")]
        public Toggle aggressiveToggle;

        [Tooltip("계산형 성향 토글입니다.")]
        public Toggle calculatedToggle;

        [Tooltip("변칙형 성향 토글입니다.")]
        public Toggle tricksterToggle;

        [Tooltip("선택 사항입니다. 연결하면 네 토글을 같은 ToggleGroup으로 자동 구성하고 Allow Switch Off를 켭니다.")]
        public ToggleGroup styleToggleGroup;

        [Header("Detail Sliders")]
        [Tooltip("0은 체크/콜 중심, 1은 레이즈와 큰 베팅을 자주 선택합니다.")]
        public Slider aggressionSlider;

        [Tooltip("0은 약한 패도 자주 참가하고, 1은 패 강도와 팟오즈를 엄격히 선별합니다.")]
        public Slider handSelectivitySlider;

        [Tooltip("0은 정직한 플레이 중심, 1은 블러프와 슬로우플레이를 자주 섞습니다.")]
        public Slider bluffTendencySlider;
    }

    [Header("Player 1~4 Home AI UI")]
    [Tooltip("0번 요소는 Player 1, 1번은 Player 2, 2번은 Player 3, 3번은 Player 4입니다.")]
    public PlayerAISettingUI[] playerSettings =
        new PlayerAISettingUI[4];

    [Header("Initial Settings")]
    [Tooltip("실행할 때 Player 1~4를 각각 보수형/공격형/계산형/변칙형으로 한 번 초기화합니다.")]
    public bool applyRecommendedDefaultsOnAwake = true;

    [Tooltip("게임 시작 시 PlayerControl에 적용된 성향값을 Console에 출력합니다.")]
    public bool logAppliedSettings = true;

    private readonly PokerAIStyle[] selectedStyles =
        new PokerAIStyle[4];

    private UnityAction<bool>[] conservativeListeners;
    private UnityAction<bool>[] aggressiveListeners;
    private UnityAction<bool>[] calculatedListeners;
    private UnityAction<bool>[] tricksterListeners;
    private UnityAction<float>[] aggressionListeners;
    private UnityAction<float>[] selectivityListeners;
    private UnityAction<float>[] bluffListeners;

    private bool isUpdatingUI;
    private bool isInitialized;

    private void Awake()
    {
        InitializeIfNeeded();
    }

    private void OnDestroy()
    {
        RemoveRuntimeListeners();
    }

    /// <summary>
    /// GameManager 또는 외부 코드에서 안전하게 초기화를 보장할 때 사용합니다.
    /// </summary>
    public void InitializeIfNeeded()
    {
        if (isInitialized)
        {
            return;
        }

        EnsurePlayerSettingArray();
        ConfigureAllControls();
        AddRuntimeListeners();

        if (applyRecommendedDefaultsOnAwake)
        {
            ApplyRecommendedDefaults();
        }
        else
        {
            ReadCurrentUIValues();
        }

        isInitialized = true;
    }

    /// <summary>
    /// Player 1=보수형, Player 2=공격형, Player 3=계산형,
    /// Player 4=변칙형으로 초기화합니다.
    /// 홈 화면을 다시 열 때 자동 호출하지 않으므로 같은 실행 중 사용자가 조절한 값은 유지됩니다.
    /// </summary>
    [ContextMenu("Apply Recommended Defaults")]
    public void ApplyRecommendedDefaults()
    {
        EnsurePlayerSettingArray();

        for (int i = 0; i < playerSettings.Length; i++)
        {
            PokerAIStyle defaultStyle = GetDefaultStyleForSlot(i);
            ApplyPresetToSlot(i, defaultStyle, true);
        }
    }

    /// <summary>
    /// 현재 홈 UI 값을 PlayerControl 1~4에 적용합니다.
    /// FiveCardDrawGameManager.StartGameFromHome()에서 자동 호출됩니다.
    /// </summary>
    public void ApplySettingsToPlayers(
        IList<PlayerControl> players)
    {
        InitializeIfNeeded();

        if (players == null)
        {
            Debug.LogWarning(
                "HomeAISettingsController: 적용할 PlayerControl 목록이 없습니다."
            );
            return;
        }

        for (int slotIndex = 0;
             slotIndex < playerSettings.Length;
             slotIndex++)
        {
            PlayerAISettingUI setting = playerSettings[slotIndex];
            int playerNumber = slotIndex + 1;

            PlayerControl player = FindPlayer(
                players,
                playerNumber
            );

            if (player == null)
            {
                Debug.LogWarning(
                    "HomeAISettingsController: Player " +
                    playerNumber +
                    "을 찾을 수 없습니다."
                );
                continue;
            }

            PokerAIStyle style = selectedStyles[slotIndex];

            float fallbackAggression = player.AIAggression;
            float fallbackSelectivity = player.AIHandSelectivity;
            float fallbackBluff = player.AIBluffTendency;

            if (style != PokerAIStyle.Custom)
            {
                GetPresetValues(
                    style,
                    out fallbackAggression,
                    out fallbackSelectivity,
                    out fallbackBluff
                );
            }

            float aggression = GetSliderValue(
                setting.aggressionSlider,
                fallbackAggression
            );

            float selectivity = GetSliderValue(
                setting.handSelectivitySlider,
                fallbackSelectivity
            );

            float bluff = GetSliderValue(
                setting.bluffTendencySlider,
                fallbackBluff
            );

            player.ApplyAISettings(
                style,
                aggression,
                selectivity,
                bluff
            );

            if (logAppliedSettings)
            {
                Debug.Log(
                    "Player " + playerNumber +
                    " AI 적용 / " + GetStyleName(style) +
                    " / 공격성 " + aggression.ToString("0.00") +
                    " / 패 선별력 " + selectivity.ToString("0.00") +
                    " / 블러프 " + bluff.ToString("0.00")
                );
            }
        }
    }

    /// <summary>
    /// 성향 토글이 변경될 때 호출됩니다.
    /// 토글이 켜지면 해당 프리셋을 적용하고 다른 성향 토글은 모두 끕니다.
    /// 선택된 토글을 직접 꺼서 아무 토글도 남지 않으면 현재 슬라이더값을 사용하는 커스텀 상태가 됩니다.
    /// </summary>
    private void HandleStyleToggleChanged(
        int slotIndex,
        PokerAIStyle style,
        bool isOn)
    {
        if (isUpdatingUI || !IsValidSlot(slotIndex))
        {
            return;
        }

        if (isOn)
        {
            ApplyPresetToSlot(slotIndex, style, true);
            return;
        }

        if (!IsAnyStyleToggleOn(playerSettings[slotIndex]))
        {
            selectedStyles[slotIndex] = PokerAIStyle.Custom;
        }
    }

    /// <summary>
    /// 슬라이더를 하나라도 직접 움직이면 해당 플레이어의 성향을 커스텀으로 바꾸고
    /// 네 개의 성향 토글을 모두 끕니다.
    /// </summary>
    private void HandleAnySliderChanged(int slotIndex)
    {
        if (isUpdatingUI || !IsValidSlot(slotIndex))
        {
            return;
        }

        selectedStyles[slotIndex] = PokerAIStyle.Custom;

        isUpdatingUI = true;
        SetStyleTogglesWithoutNotify(
            playerSettings[slotIndex],
            PokerAIStyle.Custom
        );
        isUpdatingUI = false;
    }

    private void ApplyPresetToSlot(
        int slotIndex,
        PokerAIStyle style,
        bool updateToggles)
    {
        if (!IsValidSlot(slotIndex) ||
            style == PokerAIStyle.Custom)
        {
            return;
        }

        float aggression;
        float selectivity;
        float bluff;

        GetPresetValues(
            style,
            out aggression,
            out selectivity,
            out bluff
        );

        PlayerAISettingUI setting = playerSettings[slotIndex];

        isUpdatingUI = true;
        selectedStyles[slotIndex] = style;

        if (updateToggles)
        {
            SetStyleTogglesWithoutNotify(setting, style);
        }

        SetSliderValueWithoutNotify(
            setting.aggressionSlider,
            aggression
        );

        SetSliderValueWithoutNotify(
            setting.handSelectivitySlider,
            selectivity
        );

        SetSliderValueWithoutNotify(
            setting.bluffTendencySlider,
            bluff
        );

        isUpdatingUI = false;
    }

    private void ConfigureAllControls()
    {
        for (int i = 0; i < playerSettings.Length; i++)
        {
            PlayerAISettingUI setting = playerSettings[i];

            if (setting == null)
            {
                continue;
            }

            setting.playerNumber = i + 1;

            ConfigureStyleToggles(setting);
            ConfigureSlider(setting.aggressionSlider);
            ConfigureSlider(setting.handSelectivitySlider);
            ConfigureSlider(setting.bluffTendencySlider);
        }
    }

    private static void ConfigureStyleToggles(
        PlayerAISettingUI setting)
    {
        if (setting == null)
        {
            return;
        }

        if (setting.styleToggleGroup != null)
        {
            setting.styleToggleGroup.allowSwitchOff = true;

            AssignToggleGroup(
                setting.conservativeToggle,
                setting.styleToggleGroup
            );

            AssignToggleGroup(
                setting.aggressiveToggle,
                setting.styleToggleGroup
            );

            AssignToggleGroup(
                setting.calculatedToggle,
                setting.styleToggleGroup
            );

            AssignToggleGroup(
                setting.tricksterToggle,
                setting.styleToggleGroup
            );
        }
        else
        {
            EnableAllowSwitchOff(setting.conservativeToggle);
            EnableAllowSwitchOff(setting.aggressiveToggle);
            EnableAllowSwitchOff(setting.calculatedToggle);
            EnableAllowSwitchOff(setting.tricksterToggle);
        }
    }

    private static void AssignToggleGroup(
        Toggle toggle,
        ToggleGroup group)
    {
        if (toggle == null)
        {
            return;
        }

        toggle.group = group;
    }

    private static void EnableAllowSwitchOff(Toggle toggle)
    {
        if (toggle != null && toggle.group != null)
        {
            toggle.group.allowSwitchOff = true;
        }
    }

    private void AddRuntimeListeners()
    {
        int count = playerSettings.Length;

        conservativeListeners = new UnityAction<bool>[count];
        aggressiveListeners = new UnityAction<bool>[count];
        calculatedListeners = new UnityAction<bool>[count];
        tricksterListeners = new UnityAction<bool>[count];
        aggressionListeners = new UnityAction<float>[count];
        selectivityListeners = new UnityAction<float>[count];
        bluffListeners = new UnityAction<float>[count];

        for (int i = 0; i < count; i++)
        {
            int capturedIndex = i;
            PlayerAISettingUI setting = playerSettings[i];

            conservativeListeners[i] = delegate (bool isOn)
            {
                HandleStyleToggleChanged(
                    capturedIndex,
                    PokerAIStyle.Conservative,
                    isOn
                );
            };

            aggressiveListeners[i] = delegate (bool isOn)
            {
                HandleStyleToggleChanged(
                    capturedIndex,
                    PokerAIStyle.Aggressive,
                    isOn
                );
            };

            calculatedListeners[i] = delegate (bool isOn)
            {
                HandleStyleToggleChanged(
                    capturedIndex,
                    PokerAIStyle.Calculated,
                    isOn
                );
            };

            tricksterListeners[i] = delegate (bool isOn)
            {
                HandleStyleToggleChanged(
                    capturedIndex,
                    PokerAIStyle.Trickster,
                    isOn
                );
            };

            aggressionListeners[i] = delegate (float value)
            {
                HandleAnySliderChanged(capturedIndex);
            };

            selectivityListeners[i] = delegate (float value)
            {
                HandleAnySliderChanged(capturedIndex);
            };

            bluffListeners[i] = delegate (float value)
            {
                HandleAnySliderChanged(capturedIndex);
            };

            AddToggleListener(
                setting.conservativeToggle,
                conservativeListeners[i]
            );

            AddToggleListener(
                setting.aggressiveToggle,
                aggressiveListeners[i]
            );

            AddToggleListener(
                setting.calculatedToggle,
                calculatedListeners[i]
            );

            AddToggleListener(
                setting.tricksterToggle,
                tricksterListeners[i]
            );

            if (setting.aggressionSlider != null)
            {
                setting.aggressionSlider.onValueChanged.AddListener(
                    aggressionListeners[i]
                );
            }

            if (setting.handSelectivitySlider != null)
            {
                setting.handSelectivitySlider.onValueChanged.AddListener(
                    selectivityListeners[i]
                );
            }

            if (setting.bluffTendencySlider != null)
            {
                setting.bluffTendencySlider.onValueChanged.AddListener(
                    bluffListeners[i]
                );
            }
        }
    }

    private static void AddToggleListener(
        Toggle toggle,
        UnityAction<bool> listener)
    {
        if (toggle != null && listener != null)
        {
            toggle.onValueChanged.AddListener(listener);
        }
    }

    private void RemoveRuntimeListeners()
    {
        if (playerSettings == null)
        {
            return;
        }

        for (int i = 0; i < playerSettings.Length; i++)
        {
            PlayerAISettingUI setting = playerSettings[i];

            if (setting == null)
            {
                continue;
            }

            RemoveToggleListener(
                setting.conservativeToggle,
                conservativeListeners,
                i
            );

            RemoveToggleListener(
                setting.aggressiveToggle,
                aggressiveListeners,
                i
            );

            RemoveToggleListener(
                setting.calculatedToggle,
                calculatedListeners,
                i
            );

            RemoveToggleListener(
                setting.tricksterToggle,
                tricksterListeners,
                i
            );

            if (aggressionListeners != null &&
                i < aggressionListeners.Length &&
                aggressionListeners[i] != null &&
                setting.aggressionSlider != null)
            {
                setting.aggressionSlider.onValueChanged.RemoveListener(
                    aggressionListeners[i]
                );
            }

            if (selectivityListeners != null &&
                i < selectivityListeners.Length &&
                selectivityListeners[i] != null &&
                setting.handSelectivitySlider != null)
            {
                setting.handSelectivitySlider.onValueChanged.RemoveListener(
                    selectivityListeners[i]
                );
            }

            if (bluffListeners != null &&
                i < bluffListeners.Length &&
                bluffListeners[i] != null &&
                setting.bluffTendencySlider != null)
            {
                setting.bluffTendencySlider.onValueChanged.RemoveListener(
                    bluffListeners[i]
                );
            }
        }
    }

    private static void RemoveToggleListener(
        Toggle toggle,
        UnityAction<bool>[] listeners,
        int index)
    {
        if (toggle == null ||
            listeners == null ||
            index < 0 ||
            index >= listeners.Length ||
            listeners[index] == null)
        {
            return;
        }

        toggle.onValueChanged.RemoveListener(listeners[index]);
    }

    private void ReadCurrentUIValues()
    {
        for (int i = 0; i < playerSettings.Length; i++)
        {
            PlayerAISettingUI setting = playerSettings[i];
            PokerAIStyle style = GetSelectedStyleFromToggles(setting);

            selectedStyles[i] = style;

            // 여러 토글이 잘못 켜진 씬 데이터도 한 개만 남도록 정리합니다.
            isUpdatingUI = true;
            SetStyleTogglesWithoutNotify(setting, style);
            isUpdatingUI = false;
        }
    }

    private void EnsurePlayerSettingArray()
    {
        if (playerSettings == null)
        {
            playerSettings = new PlayerAISettingUI[4];
        }
        else if (playerSettings.Length != 4)
        {
            Array.Resize(ref playerSettings, 4);
        }

        for (int i = 0; i < playerSettings.Length; i++)
        {
            if (playerSettings[i] == null)
            {
                playerSettings[i] = new PlayerAISettingUI();
            }

            playerSettings[i].playerNumber = i + 1;
        }
    }

    private static void SetStyleTogglesWithoutNotify(
        PlayerAISettingUI setting,
        PokerAIStyle selectedStyle)
    {
        if (setting == null)
        {
            return;
        }

        SetToggleValueWithoutNotify(
            setting.conservativeToggle,
            selectedStyle == PokerAIStyle.Conservative
        );

        SetToggleValueWithoutNotify(
            setting.aggressiveToggle,
            selectedStyle == PokerAIStyle.Aggressive
        );

        SetToggleValueWithoutNotify(
            setting.calculatedToggle,
            selectedStyle == PokerAIStyle.Calculated
        );

        SetToggleValueWithoutNotify(
            setting.tricksterToggle,
            selectedStyle == PokerAIStyle.Trickster
        );
    }

    private static void SetToggleValueWithoutNotify(
        Toggle toggle,
        bool value)
    {
        if (toggle != null)
        {
            toggle.SetIsOnWithoutNotify(value);
        }
    }

    private static bool IsAnyStyleToggleOn(
        PlayerAISettingUI setting)
    {
        return setting != null &&
               (IsToggleOn(setting.conservativeToggle) ||
                IsToggleOn(setting.aggressiveToggle) ||
                IsToggleOn(setting.calculatedToggle) ||
                IsToggleOn(setting.tricksterToggle));
    }

    private static bool IsToggleOn(Toggle toggle)
    {
        return toggle != null && toggle.isOn;
    }

    private static PokerAIStyle GetSelectedStyleFromToggles(
        PlayerAISettingUI setting)
    {
        if (setting == null)
        {
            return PokerAIStyle.Custom;
        }

        if (IsToggleOn(setting.conservativeToggle))
        {
            return PokerAIStyle.Conservative;
        }

        if (IsToggleOn(setting.aggressiveToggle))
        {
            return PokerAIStyle.Aggressive;
        }

        if (IsToggleOn(setting.calculatedToggle))
        {
            return PokerAIStyle.Calculated;
        }

        if (IsToggleOn(setting.tricksterToggle))
        {
            return PokerAIStyle.Trickster;
        }

        return PokerAIStyle.Custom;
    }

    private static PlayerControl FindPlayer(
        IList<PlayerControl> players,
        int playerNumber)
    {
        for (int i = 0; i < players.Count; i++)
        {
            PlayerControl player = players[i];

            if (player != null &&
                player.playerNumber == playerNumber)
            {
                return player;
            }
        }

        return null;
    }

    private static PokerAIStyle GetDefaultStyleForSlot(
        int slotIndex)
    {
        switch (slotIndex)
        {
            case 0:
                return PokerAIStyle.Conservative;

            case 1:
                return PokerAIStyle.Aggressive;

            case 2:
                return PokerAIStyle.Calculated;

            case 3:
                return PokerAIStyle.Trickster;

            default:
                return PokerAIStyle.Conservative;
        }
    }

    private static void GetPresetValues(
        PokerAIStyle style,
        out float aggression,
        out float selectivity,
        out float bluff)
    {
        switch (style)
        {
            case PokerAIStyle.Aggressive:
                aggression = 0.88f;
                selectivity = 0.42f;
                bluff = 0.58f;
                break;

            case PokerAIStyle.Calculated:
                aggression = 0.55f;
                selectivity = 0.76f;
                bluff = 0.20f;
                break;

            case PokerAIStyle.Trickster:
                aggression = 0.62f;
                selectivity = 0.50f;
                bluff = 0.88f;
                break;

            case PokerAIStyle.Conservative:
            default:
                aggression = 0.25f;
                selectivity = 0.88f;
                bluff = 0.12f;
                break;
        }
    }

    private static string GetStyleName(PokerAIStyle style)
    {
        switch (style)
        {
            case PokerAIStyle.Conservative:
                return "보수형";

            case PokerAIStyle.Aggressive:
                return "공격형";

            case PokerAIStyle.Calculated:
                return "계산형";

            case PokerAIStyle.Trickster:
                return "변칙형";

            case PokerAIStyle.Custom:
                return "커스텀";

            default:
                return style.ToString();
        }
    }

    private static void ConfigureSlider(Slider slider)
    {
        if (slider == null)
        {
            return;
        }

        slider.minValue = 0f;
        slider.maxValue = 1f;
        slider.wholeNumbers = false;
    }

    private static void SetSliderValueWithoutNotify(
        Slider slider,
        float value)
    {
        if (slider != null)
        {
            slider.SetValueWithoutNotify(
                Mathf.Clamp01(value)
            );
        }
    }

    private static float GetSliderValue(
        Slider slider,
        float fallbackValue)
    {
        return slider != null
            ? Mathf.Clamp01(slider.value)
            : Mathf.Clamp01(fallbackValue);
    }

    private bool IsValidSlot(int slotIndex)
    {
        return playerSettings != null &&
               slotIndex >= 0 &&
               slotIndex < playerSettings.Length &&
               playerSettings[slotIndex] != null;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        EnsurePlayerSettingArray();
        ConfigureAllControls();
    }
#endif
}