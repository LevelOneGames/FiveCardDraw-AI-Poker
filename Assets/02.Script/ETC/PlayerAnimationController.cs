using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 포커 플레이어 아바타의 애니메이션을 관리합니다.
///
/// Animator 구성:
/// - Breathing : 기본 상태, Loop
/// - Blink     : Breathing에서 Blink Trigger로 진입, 종료 후 Breathing 복귀
/// - Win       : Any State에서 Win Trigger로 진입, 종료 후 Breathing 복귀
/// - Lose      : Any State에서 Lose Trigger로 진입, 종료 후 Breathing 복귀
///
/// 평소에는 Breathing을 반복하고, 서로 다른 랜덤 간격으로 Blink를 실행합니다.
/// Win/Lose가 실행된 뒤에는 다음 게임이 시작될 때까지 랜덤 Blink를 잠급니다.
/// </summary>
[DisallowMultipleComponent]
public class PlayerAnimationController : MonoBehaviour
{
    private enum ResultAnimation
    {
        None = 0,
        Win = 1,
        Lose = 2
    }

    [Header("Animator")]
    [Tooltip("플레이어 캐릭터의 Animator입니다. 비워두면 현재 오브젝트와 자식에서 자동으로 찾습니다.")]
    [SerializeField] private Animator animator;

    [Header("Animator Trigger Parameters")]
    [Tooltip("눈깜박임 Trigger 파라미터 이름입니다.")]
    [SerializeField] private string blinkTriggerName = "Blink";

    [Tooltip("승리 Trigger 파라미터 이름입니다.")]
    [SerializeField] private string winTriggerName = "Win";

    [Tooltip("패배 Trigger 파라미터 이름입니다.")]
    [SerializeField] private string loseTriggerName = "Lose";

    [Header("Animator State")]
    [Tooltip("새 게임 시작 시 강제로 돌아갈 기본 숨쉬기 State 이름입니다. 기본 레이어라면 Base Layer.Breathing을 권장합니다.")]
    [SerializeField] private string breathingStateName = "Base Layer.Breathing";

    [Header("Folded Avatar Visual")]
    [Tooltip("다이 상태에서 어둡게 만들 플레이어 캐릭터 Image를 등록합니다. 여러 장으로 구성된 캐릭터라면 모두 넣어주세요.")]
    public Image[] avatarImages = new Image[0];

    [Tooltip("평상시 플레이어 이미지 색상입니다.")]
    public Color normalAvatarColor = new Color(1f, 1f, 1f, 1f);

    [Tooltip("다이한 플레이어 이미지 색상입니다.")]
    public Color foldedAvatarColor = new Color(0.5f, 0.5f, 0.5f, 1f);

    [Header("Natural Blink")]
    [Tooltip("랜덤 눈깜박임을 사용할지 결정합니다.")]
    [SerializeField] private bool useRandomBlink = true;

    [Tooltip("눈을 다시 깜박이기까지의 최소 시간입니다.")]
    [Min(0.1f)]
    [SerializeField] private float minimumBlinkInterval = 2.5f;

    [Tooltip("눈을 다시 깜박이기까지의 최대 시간입니다. 플레이어마다 시작 시간이 달라 자연스럽게 동작합니다.")]
    [Min(0.1f)]
    [SerializeField] private float maximumBlinkInterval = 7.5f;

    [Tooltip("Time.timeScale이 0이어도 눈깜박임 시간을 진행하려면 켭니다.")]
    [SerializeField] private bool useUnscaledTime;

    [Header("Debug")]
    [Tooltip("Animator, Trigger 또는 Breathing State 연결이 잘못됐을 때 경고를 출력합니다.")]
    [SerializeField] private bool logSetupWarnings = true;

    private int blinkTriggerHash;
    private int winTriggerHash;
    private int loseTriggerHash;
    private int breathingStateHash;

    private bool hasBlinkTrigger;
    private bool hasWinTrigger;
    private bool hasLoseTrigger;
    private bool hasBreathingState;

    private Coroutine blinkCoroutine;
    private ResultAnimation currentResultAnimation;
    private bool isFoldedVisual;

    /// <summary>
    /// Win 또는 Lose 결과 연출이 실행된 뒤 다음 게임 초기화를 기다리는 상태입니다.
    /// 이 상태에서는 랜덤 Blink가 발생하지 않습니다.
    /// </summary>
    public bool IsResultAnimationLocked
    {
        get { return currentResultAnimation != ResultAnimation.None; }
    }

    private void Awake()
    {
        ResolveAnimatorReference();
        ResolveAvatarImageReferences();
        CacheAnimatorHashes();
        ApplyFoldedVisualColor();
    }

    private void Start()
    {
        ValidateAnimatorSetup();
    }

    private void OnEnable()
    {
        ResolveAnimatorReference();
        ResolveAvatarImageReferences();
        CacheAnimatorHashes();
        ApplyFoldedVisualColor();
        RestartBlinkRoutine();
    }

    private void OnDisable()
    {
        StopBlinkRoutine();
    }

    /// <summary>
    /// Inspector의 Animator 참조가 비어 있을 때 자동으로 찾습니다.
    /// 먼저 현재 오브젝트를 확인하고, 없으면 비활성 자식까지 검색합니다.
    /// </summary>
    private void ResolveAnimatorReference()
    {
        if (animator != null)
        {
            return;
        }

        animator = GetComponent<Animator>();

        if (animator == null)
        {
            animator = GetComponentInChildren<Animator>(true);
        }
    }

    /// <summary>
    /// 배열이 비어 있으면 PlayerAnimationController가 붙은 같은 오브젝트의 Image를 자동으로 사용합니다.
    /// 캐릭터가 여러 Image로 구성되어 있거나 자식 Image라면 Inspector 배열에 직접 등록합니다.
    /// </summary>
    private void ResolveAvatarImageReferences()
    {
        if (avatarImages != null &&
            avatarImages.Length > 0)
        {
            return;
        }

        Image imageOnSameObject =
            GetComponent<Image>();

        if (imageOnSameObject != null)
        {
            avatarImages = new Image[]
            {
                imageOnSameObject
            };
        }
    }

    private void CacheAnimatorHashes()
    {
        blinkTriggerHash = Animator.StringToHash(blinkTriggerName);
        winTriggerHash = Animator.StringToHash(winTriggerName);
        loseTriggerHash = Animator.StringToHash(loseTriggerName);
        breathingStateHash = Animator.StringToHash(breathingStateName);

        RefreshAnimatorValidationFlags();
    }

    private void RefreshAnimatorValidationFlags()
    {
        hasBlinkTrigger = HasAnimatorParameter(
            blinkTriggerHash,
            AnimatorControllerParameterType.Trigger
        );

        hasWinTrigger = HasAnimatorParameter(
            winTriggerHash,
            AnimatorControllerParameterType.Trigger
        );

        hasLoseTrigger = HasAnimatorParameter(
            loseTriggerHash,
            AnimatorControllerParameterType.Trigger
        );

        hasBreathingState =
            animator != null &&
            animator.runtimeAnimatorController != null &&
            animator.HasState(0, breathingStateHash);

        // State 이름을 "Breathing"처럼 짧게 입력한 경우도 한 번 더 확인합니다.
        if (!hasBreathingState &&
            animator != null &&
            animator.runtimeAnimatorController != null)
        {
            string shortStateName = GetShortStateName(breathingStateName);
            int shortStateHash = Animator.StringToHash(shortStateName);

            if (animator.HasState(0, shortStateHash))
            {
                breathingStateHash = shortStateHash;
                hasBreathingState = true;
            }
        }
    }

    private bool HasAnimatorParameter(
        int parameterHash,
        AnimatorControllerParameterType expectedType)
    {
        if (animator == null ||
            animator.runtimeAnimatorController == null)
        {
            return false;
        }

        AnimatorControllerParameter[] parameters =
            animator.parameters;

        for (int i = 0; i < parameters.Length; i++)
        {
            if (parameters[i].nameHash == parameterHash &&
                parameters[i].type == expectedType)
            {
                return true;
            }
        }

        return false;
    }

    private static string GetShortStateName(string stateName)
    {
        if (string.IsNullOrEmpty(stateName))
        {
            return string.Empty;
        }

        int lastDotIndex = stateName.LastIndexOf('.');

        if (lastDotIndex < 0 ||
            lastDotIndex >= stateName.Length - 1)
        {
            return stateName;
        }

        return stateName.Substring(lastDotIndex + 1);
    }

    private void ValidateAnimatorSetup()
    {
        RefreshAnimatorValidationFlags();

        if (!logSetupWarnings)
        {
            return;
        }

        if (animator == null)
        {
            Debug.LogWarning(
                name +
                ": PlayerAnimationController에 Animator가 연결되지 않았습니다.",
                this
            );

            return;
        }

        if (animator.runtimeAnimatorController == null)
        {
            Debug.LogWarning(
                name +
                ": Animator Controller가 연결되지 않았습니다.",
                this
            );

            return;
        }

        WarnMissingTrigger(hasBlinkTrigger, blinkTriggerName);
        WarnMissingTrigger(hasWinTrigger, winTriggerName);
        WarnMissingTrigger(hasLoseTrigger, loseTriggerName);

        if (!hasBreathingState)
        {
            Debug.LogWarning(
                name +
                ": Animator 0번 레이어에서 Breathing State를 찾지 못했습니다. " +
                "PlayerAnimationController의 Breathing State Name을 확인하세요: " +
                breathingStateName,
                this
            );
        }
    }

    private void WarnMissingTrigger(
        bool exists,
        string parameterName)
    {
        if (exists)
        {
            return;
        }

        Debug.LogWarning(
            name +
            ": Animator에서 Trigger 파라미터를 찾지 못했습니다: " +
            parameterName,
            this
        );
    }

    /// <summary>
    /// 자연스러운 랜덤 간격으로 Blink Trigger를 실행합니다.
    /// 각 플레이어가 서로 다른 랜덤 대기시간을 사용하므로 동시에 깜박이지 않습니다.
    /// </summary>
    private IEnumerator BlinkRoutine()
    {
        while (true)
        {
            float waitTime = Random.Range(
                minimumBlinkInterval,
                maximumBlinkInterval
            );

            if (useUnscaledTime)
            {
                yield return new WaitForSecondsRealtime(waitTime);
            }
            else
            {
                yield return new WaitForSeconds(waitTime);
            }

            if (!CanPlayBlink())
            {
                continue;
            }

            PlayBlink();
        }
    }

    private bool CanPlayBlink()
    {
        return currentResultAnimation == ResultAnimation.None &&
               animator != null &&
               animator.isActiveAndEnabled &&
               animator.runtimeAnimatorController != null &&
               hasBlinkTrigger;
    }

    private void RestartBlinkRoutine()
    {
        StopBlinkRoutine();

        if (!isActiveAndEnabled || !useRandomBlink)
        {
            return;
        }

        blinkCoroutine = StartCoroutine(BlinkRoutine());
    }

    private void StopBlinkRoutine()
    {
        if (blinkCoroutine == null)
        {
            return;
        }

        StopCoroutine(blinkCoroutine);
        blinkCoroutine = null;
    }

    /// <summary>
    /// 즉시 Blink Trigger를 실행합니다.
    /// 결과 애니메이션이 잠겨 있는 동안에는 실행하지 않습니다.
    /// </summary>
    public void PlayBlink()
    {
        ResolveAnimatorReference();

        if (!CanPlayBlink())
        {
            return;
        }

        animator.ResetTrigger(blinkTriggerHash);
        animator.SetTrigger(blinkTriggerHash);
    }

    /// <summary>
    /// Any State -> Win 전환을 실행합니다.
    /// 동일 결과가 이미 실행된 경우 중복 Trigger를 보내지 않습니다.
    /// </summary>
    public void PlayWin()
    {
        PlayResultAnimation(ResultAnimation.Win);
    }

    /// <summary>
    /// Any State -> Lose 전환을 실행합니다.
    /// 동일 결과가 이미 실행된 경우 중복 Trigger를 보내지 않습니다.
    /// </summary>
    public void PlayLose()
    {
        PlayResultAnimation(ResultAnimation.Lose);
    }

    private void PlayResultAnimation(
        ResultAnimation resultAnimation)
    {
        ResolveAnimatorReference();

        if (animator == null ||
            !animator.isActiveAndEnabled ||
            animator.runtimeAnimatorController == null)
        {
            return;
        }

        if (currentResultAnimation == resultAnimation)
        {
            return;
        }

        RefreshAnimatorValidationFlags();

        if (resultAnimation == ResultAnimation.Win &&
            !hasWinTrigger)
        {
            return;
        }

        if (resultAnimation == ResultAnimation.Lose &&
            !hasLoseTrigger)
        {
            return;
        }

        // Blink가 예약된 상태에서 결과 애니메이션과 겹치지 않도록 모든 Trigger를 먼저 정리합니다.
        ResetAllTriggers();
        currentResultAnimation = resultAnimation;

        if (resultAnimation == ResultAnimation.Win)
        {
            animator.SetTrigger(winTriggerHash);
        }
        else
        {
            animator.SetTrigger(loseTriggerHash);
        }
    }

    /// <summary>
    /// 새 게임 시작 시 호출합니다.
    /// Win/Lose 잠금을 해제하고 기본 Breathing State로 즉시 돌아갑니다.
    /// </summary>
    public void ResetToBreathing()
    {
        ResolveAnimatorReference();
        currentResultAnimation = ResultAnimation.None;

        if (animator != null &&
            animator.runtimeAnimatorController != null)
        {
            CacheAnimatorHashes();
            ResetAllTriggers();

            if (hasBreathingState)
            {
                animator.Play(
                    breathingStateHash,
                    0,
                    0f
                );

                // UI Image의 첫 프레임을 같은 프레임에 즉시 반영합니다.
                animator.Update(0f);
            }
        }

        // 새 판 시작 직후 모든 플레이어가 동시에 눈을 깜박이지 않도록
        // 각자 새로운 랜덤 대기시간을 다시 뽑습니다.
        RestartBlinkRoutine();
    }

    private void ResetAllTriggers()
    {
        if (animator == null)
        {
            return;
        }

        if (hasBlinkTrigger)
        {
            animator.ResetTrigger(blinkTriggerHash);
        }

        if (hasWinTrigger)
        {
            animator.ResetTrigger(winTriggerHash);
        }

        if (hasLoseTrigger)
        {
            animator.ResetTrigger(loseTriggerHash);
        }
    }

    /// <summary>
    /// 다이 상태에 맞춰 등록된 모든 아바타 Image의 색상을 변경합니다.
    /// 평소에는 (1,1,1,1), 다이 시에는 기본값 (0.5,0.5,0.5,1)을 사용합니다.
    /// </summary>
    public void SetFoldedVisual(bool folded)
    {
        isFoldedVisual = folded;
        ResolveAvatarImageReferences();
        ApplyFoldedVisualColor();
    }

    private void ApplyFoldedVisualColor()
    {
        if (avatarImages == null)
        {
            return;
        }

        Color targetColor =
            isFoldedVisual
                ? foldedAvatarColor
                : normalAvatarColor;

        for (int i = 0; i < avatarImages.Length; i++)
        {
            if (avatarImages[i] != null)
            {
                avatarImages[i].color = targetColor;
            }
        }
    }

    /// <summary>
    /// 환경설정 등에서 랜덤 눈깜박임을 켜거나 끌 때 사용할 수 있습니다.
    /// </summary>
    public void SetRandomBlinkEnabled(bool enabled)
    {
        useRandomBlink = enabled;
        RestartBlinkRoutine();
    }

    [ContextMenu("Test Blink")]
    private void TestBlink()
    {
        PlayBlink();
    }

    [ContextMenu("Test Win")]
    private void TestWin()
    {
        PlayWin();
    }

    [ContextMenu("Test Lose")]
    private void TestLose()
    {
        PlayLose();
    }

    [ContextMenu("Reset To Breathing")]
    private void TestResetToBreathing()
    {
        ResetToBreathing();
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        minimumBlinkInterval =
            Mathf.Max(0.1f, minimumBlinkInterval);

        maximumBlinkInterval =
            Mathf.Max(minimumBlinkInterval, maximumBlinkInterval);

        ResolveAnimatorReference();
        ResolveAvatarImageReferences();
        CacheAnimatorHashes();
        ApplyFoldedVisualColor();
    }
#endif
}