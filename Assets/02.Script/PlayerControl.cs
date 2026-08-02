using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

public class PlayerControl : MonoBehaviour
{
    [Header("Player Identity")]
    [Tooltip("사용자는 0, 컴퓨터는 1~4로 지정합니다.")]
    public int playerNumber;

    [Tooltip("이 플레이어 행동 음성의 성별입니다.")]
    public PlayerVoiceGender voiceGender = PlayerVoiceGender.Male;

    [Header("Computer AI Personality")]
    [Tooltip("Player 1~4의 플레이 스타일입니다. 보수형/공격형/계산형/변칙형은 프리셋이며, 슬라이더를 직접 조절하면 커스텀으로 설정할 수 있습니다.")]
    public PokerAIStyle aiStyle = PokerAIStyle.Conservative;

    [Tooltip("0은 체크/콜 중심, 1은 레이즈와 큰 베팅을 매우 자주 선택합니다.")]
    [Range(0f, 1f)]
    public float aiAggression = 0.25f;

    [Tooltip("0은 약한 패도 자주 따라가고, 1은 팟오즈와 패 강도를 엄격하게 선별합니다.")]
    [Range(0f, 1f)]
    public float aiHandSelectivity = 0.88f;

    [Tooltip("0은 거의 정직하게 플레이하고, 1은 블러프와 슬로우플레이를 자주 섞습니다.")]
    [Range(0f, 1f)]
    public float aiBluffTendency = 0.12f;

    [Tooltip("Inspector에서 AI Style이 바뀔 때 권장값을 자동 적용합니다. 런타임 환경설정 Slider로 값을 바꾸는 것에는 영향을 주지 않습니다.")]
    public bool applyAIStylePresetWhenStyleChanges = true;

    [SerializeField, HideInInspector]
    private int lastAppliedAIStyle = -1;

    [Tooltip("홈 화면에서 새 게임 세션을 시작할 때 지급할 시작 금액")]
    public long startingMoney = 100_000_000L;

    public FiveCardDrawGameManager gameManager;

    [Header("Role Icons")]
    public GameObject dealerIcon;
    public GameObject smallBlindIcon;
    public GameObject bigBlindIcon;

    [Header("Turn UI")]
    public GameObject turnIndicatorObject;

    [Tooltip("턴 인디케이터와 동일한 타이밍에 켜지고 꺼지는 배경 오브젝트입니다.")]
    public GameObject indicatorBack;

    [Header("Betting Action Icon UI")]
    [Tooltip("이 플레이어의 다이, 삥, 따당, 콜, 체크, 쿼터, 하프, 올인, 맥스 아이콘을 표시할 Image입니다. Image가 붙은 GameObject는 활성화된 상태로 두세요.")]
    public Image bettingActionIconImage;

    [Header("Result UI")]
    [Tooltip("게임 종료 후 팟을 획득한 플레이어에게 표시할 위너 박스입니다.")]
    public GameObject winnerObject;

    [Tooltip("위너 오브젝트와 동일한 타이밍에 켜지고 꺼지는 위너 라인입니다.")]
    public GameObject winnerLine;

    [Tooltip("위너 박스 아래에서 이번 판 순이익을 표시할 텍스트입니다. 비워두면 위너 박스 하위에서 자동으로 찾습니다.")]
    public Text winnerAmountText;

    [Tooltip("게임 종료 후 팟을 획득하지 못한 플레이어에게 표시할 루즈 박스입니다.")]
    public GameObject loserObject;

    [Tooltip("루즈 박스 아래에서 이번 판 순손실을 표시할 텍스트입니다. 비워두면 루즈 박스 하위에서 자동으로 찾습니다.")]
    public Text loserAmountText;

    [Header("All-In UI")]
    [Tooltip("이 플레이어가 이번 판에 올인하면 켜지고, 다음 판이 시작될 때 꺼지는 올인 박스입니다.")]
    public GameObject allInObject;

    [Header("Player Animation")]
    [Tooltip("숨쉬기, 랜덤 눈깜박임, 승리, 패배 애니메이션을 관리하는 컴포넌트입니다. 비워두면 현재 오브젝트와 자식에서 자동으로 찾습니다.")]
    public PlayerAnimationController playerAnimationController;

    [Header("Exchange Selection Audio")]
    [Tooltip("Player 0이 교환할 카드를 선택하거나 선택 취소할 때 사용할 PokerAudioManager입니다. 비워두면 GameManager의 Audio Manager 또는 씬에서 자동으로 찾습니다.")]
    public PokerAudioManager exchangeSelectionAudioManager;

    [Header("Legacy Text UI")]
    public Text currentMoneyText;
    public Text roundBetMoneyText;
    public Text exchangeCountText;

    [Header("Hand Rank UI")]
    [Tooltip("족보 또는 다이 상태를 표시할 박스 오브젝트입니다.")]
    public GameObject handRankBox;

    [Tooltip("하이카드, 원페어, 투페어 등의 족보명을 표시합니다.")]
    public Text handRankText;

    [Header("Runtime Money")]
    [SerializeField] private long currentMoney;
    [SerializeField] private long roundBetMoney;
    [SerializeField] private long totalBetThisGame;

    [Tooltip("이번 판이 시작될 때의 보유금액입니다. 결과 순이익/순손실 계산에 사용합니다.")]
    [SerializeField] private long moneyAtGameStart;

    [Header("Runtime State")]
    [SerializeField] private bool isCurrentTurn;
    [SerializeField] private bool isFolded;
    [SerializeField] private bool isAllIn;
    [SerializeField] private bool hasActedThisBettingRound;
    [SerializeField] private bool hasExchangedThisGame;
    [SerializeField] private int exchangedCardCount;

    [Tooltip("이번 판에서 메인팟 또는 사이드팟을 한 번이라도 획득했는지 나타냅니다.")]
    [SerializeField] private bool isWinnerThisGame;

    [Tooltip("이번 판 도중 한 번이라도 올인했는지 기록합니다. 정산으로 돈을 받아도 다음 판 전까지 올인 박스를 유지하는 데 사용합니다.")]
    [SerializeField] private bool hasGoneAllInThisGame;

    // Player 0의 중요 UI가 외부 애니메이션이나 다른 UI 갱신으로 우연히 꺼져도
    // 현재 게임 상태에 맞는 표시를 다시 복원하기 위한 목표 상태입니다.
    private bool desiredHandRankVisible;
    private string desiredHandRankText = string.Empty;
    private bool desiredWinnerVisible;
    private bool desiredWinnerLineVisible;
    private bool desiredLoserVisible;

    // 카드 자체 클릭과 기존 Button OnClick이 같은 프레임에 함께 호출되어도
    // 동일 카드가 두 번 토글되지 않도록 마지막 요청을 기억합니다.
    private int lastExchangeToggleFrame = -1;
    private int lastExchangeToggleHandIndex = -1;

    private bool hasLoggedMissingExchangeAudioManager;

    [Header("Cards")]
    [Tooltip("이 플레이어가 현재 가지고 있는 카드 번호입니다. 0~51을 사용합니다.")]
    public List<int> cardNumbers =
        new List<int>(5);

    [Tooltip("카드가 도착할 손패 위치 5개를 0번부터 순서대로 등록합니다.")]
    public RectTransform[] cardPositions =
        new RectTransform[5];

    public List<int> selectedExchangeIndexes =
        new List<int>(3);

    // 이전에 아웃라인을 켠 카드 번호를 기억합니다.
    // 교환 또는 다음 게임에서 손패 리스트에서 빠진 카드도 정확히 끌 수 있습니다.
    private readonly List<int> outlinedCardNumbers =
        new List<int>(5);

    public long CurrentMoney
    {
        get { return currentMoney; }
    }

    public long RoundBetMoney
    {
        get { return roundBetMoney; }
    }

    public long TotalBetThisGame
    {
        get { return totalBetThisGame; }
    }

    public long MoneyAtGameStart
    {
        get { return moneyAtGameStart; }
    }

    public long NetMoneyChangeThisGame
    {
        get { return currentMoney - moneyAtGameStart; }
    }

    public bool IsCurrentTurn
    {
        get { return isCurrentTurn; }
    }

    public bool IsFolded
    {
        get { return isFolded; }
    }

    public bool IsAllIn
    {
        get { return isAllIn; }
    }

    public bool HasGoneAllInThisGame
    {
        get { return hasGoneAllInThisGame; }
    }

    public bool HasActedThisBettingRound
    {
        get { return hasActedThisBettingRound; }
    }

    public bool HasExchangedThisGame
    {
        get { return hasExchangedThisGame; }
    }

    public bool IsWinnerThisGame
    {
        get { return isWinnerThisGame; }
    }

    public int ExchangedCardCount
    {
        get { return exchangedCardCount; }
    }

    public int SelectedExchangeCardCount
    {
        get { return selectedExchangeIndexes.Count; }
    }

    public bool IsHumanPlayer
    {
        get { return playerNumber == 0; }
    }

    public bool IsComputerPlayer
    {
        get
        {
            return playerNumber >= 1 &&
                   playerNumber <= 4;
        }
    }

    public PokerAIStyle AIStyle
    {
        get { return aiStyle; }
    }

    public float AIAggression
    {
        get { return aiAggression; }
    }

    public float AIHandSelectivity
    {
        get { return aiHandSelectivity; }
    }

    public float AIBluffTendency
    {
        get { return aiBluffTendency; }
    }

    /// <summary>
    /// 현재 PlayerControl의 스타일과 3개 Slider 값을 AI 판단용 복사본으로 반환합니다.
    /// </summary>
    public PokerAIParameters GetAIParameters()
    {
        return new PokerAIParameters
        {
            style = aiStyle,
            aggression = Mathf.Clamp01(aiAggression),
            handSelectivity = Mathf.Clamp01(aiHandSelectivity),
            bluffTendency = Mathf.Clamp01(aiBluffTendency)
        };
    }

    /// <summary>
    /// 환경설정 성향 토글 등에서 스타일을 변경할 때 사용합니다.
    /// applyPreset이 true이면 해당 스타일의 권장 3개 값도 함께 적용합니다.
    /// </summary>
    public void SetAIStyle(
        PokerAIStyle style,
        bool applyPreset = true)
    {
        aiStyle = style;

        if (applyPreset && aiStyle != PokerAIStyle.Custom)
        {
            ApplyAIStylePreset();
        }
        else
        {
            lastAppliedAIStyle = (int)aiStyle;
        }
    }

    /// <summary>
    /// 환경설정 Slider OnValueChanged(float)에 연결할 수 있습니다.
    /// </summary>
    public void SetAIStyleByIndex(int styleIndex)
    {
        styleIndex = Mathf.Clamp(
            styleIndex,
            (int)PokerAIStyle.Conservative,
            (int)PokerAIStyle.Custom
        );

        PokerAIStyle style = (PokerAIStyle)styleIndex;

        SetAIStyle(
            style,
            style != PokerAIStyle.Custom
        );
    }

    /// <summary>
    /// 저장된 세부 Slider 값을 따로 복원할 때 사용합니다.
    /// 스타일만 바꾸고 프리셋 값은 다시 적용하지 않습니다.
    /// </summary>
    public void SetAIStyleByIndexWithoutPreset(int styleIndex)
    {
        styleIndex = Mathf.Clamp(
            styleIndex,
            (int)PokerAIStyle.Conservative,
            (int)PokerAIStyle.Custom
        );

        SetAIStyle(
            (PokerAIStyle)styleIndex,
            false
        );
    }

    public void SetAIAggression(float value)
    {
        aiAggression = Mathf.Clamp01(value);
    }

    public void SetAIHandSelectivity(float value)
    {
        aiHandSelectivity = Mathf.Clamp01(value);
    }

    public void SetAIBluffTendency(float value)
    {
        aiBluffTendency = Mathf.Clamp01(value);
    }

    /// <summary>
    /// 홈 화면에서 확정한 성향 토글/슬라이더 값을 한 번에 적용합니다.
    /// 커스텀은 별도 프리셋을 덮어쓰지 않고 전달된 세부값을 그대로 사용합니다.
    /// </summary>
    public void ApplyAISettings(
        PokerAIStyle style,
        float aggression,
        float handSelectivity,
        float bluffTendency)
    {
        aiStyle = style;
        aiAggression = Mathf.Clamp01(aggression);
        aiHandSelectivity = Mathf.Clamp01(handSelectivity);
        aiBluffTendency = Mathf.Clamp01(bluffTendency);
        lastAppliedAIStyle = (int)aiStyle;
    }

    /// <summary>
    /// 선택한 스타일의 권장 기본값을 적용합니다.
    /// 이후 각각의 값을 자유롭게 조절해도 됩니다.
    /// </summary>
    [ContextMenu("Apply AI Style Preset")]
    public void ApplyAIStylePreset()
    {
        switch (aiStyle)
        {
            case PokerAIStyle.Conservative:
                aiAggression = 0.25f;
                aiHandSelectivity = 0.88f;
                aiBluffTendency = 0.12f;
                break;

            case PokerAIStyle.Aggressive:
                aiAggression = 0.88f;
                aiHandSelectivity = 0.42f;
                aiBluffTendency = 0.58f;
                break;

            case PokerAIStyle.Calculated:
                aiAggression = 0.55f;
                aiHandSelectivity = 0.76f;
                aiBluffTendency = 0.20f;
                break;

            case PokerAIStyle.Trickster:
                aiAggression = 0.62f;
                aiHandSelectivity = 0.50f;
                aiBluffTendency = 0.88f;
                break;

            case PokerAIStyle.Custom:
                // 사용자가 직접 조절한 현재 슬라이더 값을 유지합니다.
                break;
        }

        lastAppliedAIStyle = (int)aiStyle;
    }

    private void Awake()
    {
        ResolveResultTextReferences();
        ResolvePlayerAnimationReference();
        ResolveExchangeSelectionAudioManager();
        ApplyFoldedVisual(isFolded);

        // 씬에서 족보박스나 결과 표시가 켜진 상태로 저장되어 있어도 게임 시작 시 숨깁니다.
        HideHandRank();
        ResetGameResultUI();
        ClearBettingActionIcon();
        RefreshAllUI();
    }

    /// <summary>
    /// Player 0의 족보박스와 최종 결과박스가 진행 중 우연히 비활성화되는 현상을 막습니다.
    /// 모든 표시 여부는 Show/Hide 함수가 저장한 목표 상태만 따르므로
    /// 새 판, 교환 중, 쇼다운 대기 등 정상적으로 숨겨야 하는 시점에는 다시 켜지지 않습니다.
    /// </summary>
    private void LateUpdate()
    {
        if (playerNumber != 0)
        {
            return;
        }

        RestoreCriticalUIVisibility();
    }

    #region Initialization

    public void InitializeForSession()
    {
        currentMoney =
            Math.Max(0L, startingMoney);

        ResetForNewGame();
    }

    /// <summary>
    /// 다음 게임 시작 직전에 새 판 데이터만 초기화합니다.
    /// 보유 금액이 0이어도 자동으로 충전하지 않습니다.
    /// 파산 플레이어의 퇴장과 새 세션 충전은 GameManager가 담당합니다.
    /// </summary>
    public void PrepareForNextGame()
    {
        ResetForNewGame();
    }

    /// <summary>
    /// 새 판 데이터만 초기화합니다.
    /// 보유 금액은 유지합니다.
    /// </summary>
    public void ResetForNewGame()
    {
        // 블라인드와 베팅이 빠져나가기 전 보유금액을 이번 판 기준금액으로 저장합니다.
        moneyAtGameStart = currentMoney;

        roundBetMoney = 0L;
        totalBetThisGame = 0L;

        isCurrentTurn = false;
        isFolded = false;
        isAllIn = false;
        hasGoneAllInThisGame = false;
        ApplyFoldedVisual(false);

        hasActedThisBettingRound = false;
        hasExchangedThisGame = false;

        exchangedCardCount = 0;

        // 이전 판에 받은 카드 초기화
        cardNumbers.Clear();

        // 이전 판의 교환 선택 초기화
        selectedExchangeIndexes.Clear();

        // 새 판이 시작될 때 전판의 족보, 다이, 위너/루즈 결과 표시를 숨깁니다.
        HideHandRank();
        ResetGameResultUI();
        ClearBettingActionIcon();

        SetRoleIcons(
            false,
            false,
            false
        );

        SetTurn(false);

        RefreshAllUI();
    }

    public void PrepareForBettingRound(
        bool resetRoundBetMoney)
    {
        hasActedThisBettingRound = false;

        if (resetRoundBetMoney)
        {
            roundBetMoney = 0L;
        }

        RefreshRoundBetMoneyUI();
    }

    public void PrepareForExchangePhase()
    {
        hasExchangedThisGame = false;
        exchangedCardCount = 0;

        // 사람 플레이어는 첫 베팅 중 미리 선택해 둔 카드를
        // 교환 페이즈에서도 그대로 사용할 수 있도록 선택을 유지합니다.
        // AI는 교환 판단을 별도로 만들기 때문에 이전 선택값을 비웁니다.
        if (!IsHumanPlayer)
        {
            selectedExchangeIndexes.Clear();
        }
        else
        {
            RemoveInvalidCardSelections();
        }

        RefreshExchangeCountUI();
    }

    #endregion

    #region Role And Turn

    public void SetRoleIcons(
        bool isDealer,
        bool isSmallBlind,
        bool isBigBlind)
    {
        if (dealerIcon != null)
        {
            dealerIcon.SetActive(isDealer);
        }

        if (smallBlindIcon != null)
        {
            smallBlindIcon.SetActive(
                isSmallBlind
            );
        }

        if (bigBlindIcon != null)
        {
            bigBlindIcon.SetActive(
                isBigBlind
            );
        }
    }


    /// <summary>
    /// 게임매니저가 전달한 행동 아이콘을 표시합니다.
    /// 스프라이트가 없으면 Image를 숨깁니다.
    /// </summary>
    public void SetBettingActionIcon(Sprite sprite)
    {
        if (bettingActionIconImage == null)
        {
            return;
        }

        bettingActionIconImage.sprite = sprite;
        bettingActionIconImage.enabled = sprite != null;
    }

    /// <summary>
    /// 현재 표시 중인 행동 아이콘을 지웁니다.
    /// </summary>
    public void ClearBettingActionIcon()
    {
        if (bettingActionIconImage == null)
        {
            return;
        }

        bettingActionIconImage.sprite = null;
        bettingActionIconImage.enabled = false;
    }

    public void SetTurn(bool value)
    {
        isCurrentTurn = value;

        if (turnIndicatorObject != null)
        {
            turnIndicatorObject.SetActive(value);
        }

        if (indicatorBack != null)
        {
            indicatorBack.SetActive(value);
        }
    }

    public void SetFolded(bool value)
    {
        isFolded = value;
        ApplyFoldedVisual(value);

        if (value)
        {
            selectedExchangeIndexes.Clear();
            SetTurn(false);
            ShowFoldedHandRank();
        }
    }

    /// <summary>
    /// PlayerAnimationController에 등록된 아바타 Image 색상을
    /// 평소 흰색 또는 다이 상태의 어두운 색상으로 전환합니다.
    /// </summary>
    private void ApplyFoldedVisual(bool folded)
    {
        ResolvePlayerAnimationReference();

        if (playerAnimationController != null)
        {
            playerAnimationController.SetFoldedVisual(folded);
        }
    }

    public void SetActedThisBettingRound(
        bool value)
    {
        hasActedThisBettingRound = value;
    }

    /// <summary>
    /// 이번 판에서 메인팟 또는 사이드팟을 획득했는지 저장합니다.
    /// 실제 위너/루즈 박스와 금액 표시는 모든 팟 정산이 끝난 뒤 ShowFinalGameResult에서 갱신합니다.
    /// </summary>
    public void SetWinner(bool value)
    {
        isWinnerThisGame = value;

        if (!value)
        {
            HideResultBoxes();
        }
    }

    /// <summary>
    /// 쇼다운 정산 후 이번 판의 최종 위너/루즈 박스와 순변동 금액을 표시합니다.
    /// 위너는 시작금액 대비 순이익, 루즈는 시작금액 대비 순손실을 표시합니다.
    /// </summary>
    public void ShowFinalGameResult()
    {
        ResolveResultTextReferences();

        long netChange = NetMoneyChangeThisGame;

        desiredWinnerVisible = isWinnerThisGame;
        desiredWinnerLineVisible = isWinnerThisGame;
        desiredLoserVisible = !isWinnerThisGame;
        ApplyDesiredResultVisibility();

        if (isWinnerThisGame)
        {
            if (winnerAmountText != null)
            {
                long gainedAmount = Math.Max(0L, netChange);
                winnerAmountText.text = FormatKoreanMoney(gainedAmount);
            }

            if (loserAmountText != null)
            {
                loserAmountText.text = string.Empty;
            }
        }
        else
        {
            if (winnerAmountText != null)
            {
                winnerAmountText.text = string.Empty;
            }

            if (loserAmountText != null)
            {
                long lostAmount = Math.Max(0L, -netChange);
                loserAmountText.text =
                    "- " + FormatKoreanMoney(lostAmount);
            }
        }

        // 게임매니저가 최종 결과 UI를 표시하는 바로 그 시점에
        // 위너는 Win, 나머지 플레이어는 Lose 애니메이션을 실행합니다.
        if (isWinnerThisGame)
        {
            PlayWinAnimation();
        }
        else
        {
            PlayLoseAnimation();
        }
    }

    /// <summary>
    /// 새 게임 또는 새 결과 판정 전에 위너/루즈 박스와 금액 텍스트를 초기화합니다.
    /// </summary>
    public void ResetGameResultUI()
    {
        isWinnerThisGame = false;
        HideResultBoxes();

        ResolveResultTextReferences();

        if (winnerAmountText != null)
        {
            winnerAmountText.text = string.Empty;
        }

        if (loserAmountText != null)
        {
            loserAmountText.text = string.Empty;
        }

        // 새 판이 시작되거나 결과 표시를 초기화할 때는
        // 이전 Win/Lose 상태를 끝내고 기본 숨쉬기 상태로 복귀합니다.
        ResetPlayerAnimation();
    }

    /// <summary>
    /// 이 플레이어의 승리 애니메이션을 실행합니다.
    /// GameManager 외의 연출 스크립트에서도 직접 호출할 수 있습니다.
    /// </summary>
    public void PlayWinAnimation()
    {
        ResolvePlayerAnimationReference();

        if (playerAnimationController != null)
        {
            playerAnimationController.PlayWin();
        }
    }

    /// <summary>
    /// 이 플레이어의 패배 애니메이션을 실행합니다.
    /// 다이한 플레이어도 최종 결과 표시 시 Lose 애니메이션을 실행합니다.
    /// </summary>
    public void PlayLoseAnimation()
    {
        ResolvePlayerAnimationReference();

        if (playerAnimationController != null)
        {
            playerAnimationController.PlayLose();
        }
    }

    /// <summary>
    /// 결과 애니메이션과 남아 있는 Trigger를 초기화하고
    /// 기본 Breathing 상태로 돌아갑니다.
    /// </summary>
    public void ResetPlayerAnimation()
    {
        ResolvePlayerAnimationReference();

        if (playerAnimationController != null)
        {
            playerAnimationController.ResetToBreathing();
        }
    }

    private void HideResultBoxes()
    {
        desiredWinnerVisible = false;
        desiredWinnerLineVisible = false;
        desiredLoserVisible = false;
        ApplyDesiredResultVisibility();
    }

    private void ApplyDesiredResultVisibility()
    {
        SetGameObjectActiveIfNeeded(
            winnerObject,
            desiredWinnerVisible
        );

        SetGameObjectActiveIfNeeded(
            winnerLine,
            desiredWinnerLineVisible
        );

        SetGameObjectActiveIfNeeded(
            loserObject,
            desiredLoserVisible
        );
    }

    /// <summary>
    /// 파산으로 자리에서 빠질 때 PlayerControl 오브젝트 밖에 배치된 UI까지 확실히 정리합니다.
    /// GameManager.RemovePlayerFromSeat()에서 비활성화 직전에 호출합니다.
    /// </summary>
    public void PrepareForSeatExit()
    {
        SetTurn(false);
        HideHandRank();
        ResetGameResultUI();
        ClearBettingActionIcon();

        isAllIn = false;
        hasGoneAllInThisGame = false;
        RefreshAllInUI();
    }

    private void ResolveResultTextReferences()
    {
        if (winnerAmountText == null && winnerObject != null)
        {
            winnerAmountText =
                winnerObject.GetComponentInChildren<Text>(true);
        }

        if (loserAmountText == null && loserObject != null)
        {
            loserAmountText =
                loserObject.GetComponentInChildren<Text>(true);
        }
    }

    private void ResolvePlayerAnimationReference()
    {
        if (playerAnimationController != null)
        {
            return;
        }

        playerAnimationController =
            GetComponent<PlayerAnimationController>();

        if (playerAnimationController == null)
        {
            playerAnimationController =
                GetComponentInChildren<PlayerAnimationController>(true);
        }
    }

    #endregion

    #region Money

    public void SetCurrentMoney(long amount)
    {
        currentMoney = Math.Max(0L, amount);

        if (currentMoney > 0L)
        {
            isAllIn = false;
        }

        RefreshCurrentMoneyUI();
    }

    public void AddMoney(long amount)
    {
        currentMoney += amount;

        if (currentMoney < 0L)
        {
            currentMoney = 0L;
        }

        if (currentMoney > 0L)
        {
            isAllIn = false;
        }

        RefreshCurrentMoneyUI();
    }

    /// <summary>
    /// 실제로 판돈에 넣을 금액을 적용합니다.
    /// </summary>
    public long CommitBet(long requestedAmount)
    {
        if (requestedAmount <= 0L ||
            currentMoney <= 0L)
        {
            return 0L;
        }

        long actualAmount = Math.Min(
            requestedAmount,
            currentMoney
        );

        currentMoney -= actualAmount;
        roundBetMoney += actualAmount;
        totalBetThisGame += actualAmount;

        if (currentMoney <= 0L)
        {
            currentMoney = 0L;
            isAllIn = true;
            hasGoneAllInThisGame = true;
        }

        RefreshCurrentMoneyUI();
        RefreshRoundBetMoneyUI();
        RefreshAllInUI();

        return actualAmount;
    }

    #endregion

    #region Betting Buttons

    private void SubmitBettingAction(
        BettingAction action)
    {
        if (gameManager == null)
        {
            Debug.LogError(
                name +
                ": 게임매니저가 연결되지 않았습니다."
            );

            return;
        }

        if (!isCurrentTurn)
        {
            Debug.LogWarning(
                "현재 Player " +
                playerNumber +
                "의 턴이 아닙니다."
            );

            return;
        }

        gameManager.SubmitBettingAction(
            this,
            action
        );
    }

    public void FoldButton()
    {
        SubmitBettingAction(
            BettingAction.Fold
        );
    }

    public void PingButton()
    {
        SubmitBettingAction(
            BettingAction.Ping
        );
    }

    public void DoubleButton()
    {
        SubmitBettingAction(
            BettingAction.Double
        );
    }

    public void CallButton()
    {
        SubmitBettingAction(
            BettingAction.Call
        );
    }

    public void CheckButton()
    {
        SubmitBettingAction(
            BettingAction.Check
        );
    }

    public void QuarterButton()
    {
        SubmitBettingAction(
            BettingAction.Quarter
        );
    }

    public void HalfButton()
    {
        SubmitBettingAction(
            BettingAction.Half
        );
    }

    public void AllInButton()
    {
        SubmitBettingAction(
            BettingAction.AllIn
        );
    }

    public void MaxButton()
    {
        SubmitBettingAction(
            BettingAction.Max
        );
    }

    // 아래 함수들은 Unity Toggle의 OnValueChanged(bool)에 연결합니다.
    // 상대 턴에는 예약하고, 내 턴이면 게임매니저가 즉시 실행합니다.

    private void HandleBettingToggleChanged(
        BettingAction action,
        bool isOn)
    {
        if (gameManager == null)
        {
            Debug.LogError(
                name +
                ": 게임매니저가 연결되지 않았습니다."
            );

            return;
        }

        gameManager.HandleHumanBettingToggleChanged(
            this,
            action,
            isOn
        );
    }

    public void FoldToggleChanged(bool isOn)
    {
        HandleBettingToggleChanged(
            BettingAction.Fold,
            isOn
        );
    }

    public void PingToggleChanged(bool isOn)
    {
        HandleBettingToggleChanged(
            BettingAction.Ping,
            isOn
        );
    }

    public void DoubleToggleChanged(bool isOn)
    {
        HandleBettingToggleChanged(
            BettingAction.Double,
            isOn
        );
    }

    public void CallToggleChanged(bool isOn)
    {
        HandleBettingToggleChanged(
            BettingAction.Call,
            isOn
        );
    }

    public void CheckToggleChanged(bool isOn)
    {
        HandleBettingToggleChanged(
            BettingAction.Check,
            isOn
        );
    }

    public void QuarterToggleChanged(bool isOn)
    {
        HandleBettingToggleChanged(
            BettingAction.Quarter,
            isOn
        );
    }

    public void HalfToggleChanged(bool isOn)
    {
        HandleBettingToggleChanged(
            BettingAction.Half,
            isOn
        );
    }

    public void AllInToggleChanged(bool isOn)
    {
        HandleBettingToggleChanged(
            BettingAction.AllIn,
            isOn
        );
    }

    public void MaxToggleChanged(bool isOn)
    {
        HandleBettingToggleChanged(
            BettingAction.Max,
            isOn
        );
    }

    private void HandleExchangeToggleChanged(
        ExchangeAction action,
        bool isOn)
    {
        if (gameManager == null)
        {
            Debug.LogError(
                name +
                ": 게임매니저가 연결되지 않았습니다."
            );

            return;
        }

        gameManager.HandleHumanExchangeToggleChanged(
            this,
            action,
            isOn
        );
    }

    /// <summary>
    /// 교환하지 않고 진행하는 패스 토글의 OnValueChanged(bool)에 연결합니다.
    /// </summary>
    public void PassToggleChanged(bool isOn)
    {
        HandleExchangeToggleChanged(
            ExchangeAction.Pass,
            isOn
        );
    }

    /// <summary>
    /// 선택한 카드를 교환하는 교환 토글의 OnValueChanged(bool)에 연결합니다.
    /// </summary>
    public void ExchangeToggleChanged(bool isOn)
    {
        HandleExchangeToggleChanged(
            ExchangeAction.Exchange,
            isOn
        );
    }

    #endregion

    #region Cards And Exchange

    public void ReceiveCard(int cardNumber)
    {
        if (!CardUtility.IsValidCardNumber(
                cardNumber))
        {
            Debug.LogError(
                "잘못된 카드 번호: " +
                cardNumber
            );

            return;
        }

        if (cardNumbers.Count >= 5)
        {
            Debug.LogWarning(
                "Player " +
                playerNumber +
                "는 이미 카드 5장을 가지고 있습니다."
            );

            return;
        }

        cardNumbers.Add(cardNumber);

        // 족보와 아웃라인은 최초 5장 분배가 모두 끝나고
        // 손패 정렬까지 완료된 뒤 게임매니저에서 갱신합니다.
    }

    /// <summary>
    /// 현재 손패를 숫자가 작은 카드부터 정렬합니다.
    /// 숫자가 같으면 무늬를 ♣, ♦, ♥, ♠ 순서로 정렬합니다.
    /// 카드 이동은 CardControl이 변경된 리스트 인덱스를 감지하여 자연스럽게 처리합니다.
    /// </summary>
    public void SortCardsByRankThenSuit()
    {
        if (cardNumbers == null ||
            cardNumbers.Count <= 1)
        {
            return;
        }

        cardNumbers.Sort(
            delegate (int cardA, int cardB)
            {
                bool isValidA =
                    CardUtility.IsValidCardNumber(cardA);

                bool isValidB =
                    CardUtility.IsValidCardNumber(cardB);

                // 교환 중 빈 슬롯(-1)이 있을 경우에는
                // 유효한 카드를 먼저, 빈 슬롯을 뒤로 보냅니다.
                if (!isValidA || !isValidB)
                {
                    if (isValidA == isValidB)
                    {
                        return cardA.CompareTo(cardB);
                    }

                    return isValidA ? -1 : 1;
                }

                int rankA =
                    (int)CardUtility.GetRank(cardA);

                int rankB =
                    (int)CardUtility.GetRank(cardB);

                int rankCompare =
                    rankA.CompareTo(rankB);

                if (rankCompare != 0)
                {
                    return rankCompare;
                }

                int suitA =
                    (int)CardUtility.GetSuit(cardA);

                int suitB =
                    (int)CardUtility.GetSuit(cardB);

                return suitA.CompareTo(suitB);
            }
        );
    }

    public void ReplaceCardAt(
        int handIndex,
        int newCardNumber)
    {
        if (handIndex < 0 ||
            handIndex >= cardNumbers.Count)
        {
            Debug.LogError(
                "잘못된 손패 인덱스: " +
                handIndex
            );

            return;
        }

        if (!CardUtility.IsValidCardNumber(
                newCardNumber))
        {
            Debug.LogError(
                "잘못된 카드 번호: " +
                newCardNumber
            );

            return;
        }

        cardNumbers[handIndex] =
            newCardNumber;
    }

    /// <summary>
    /// 교환 연출을 위해 해당 손패 자리를 잠시 비웁니다.
    /// 반환된 기존 카드 번호는 버린 카드 더미로 이동시킬 때 사용합니다.
    /// </summary>
    public int RemoveCardAtForExchange(
        int handIndex)
    {
        if (handIndex < 0 ||
            handIndex >= cardNumbers.Count)
        {
            Debug.LogError(
                "잘못된 손패 인덱스: " +
                handIndex
            );

            return -1;
        }

        int oldCardNumber =
            cardNumbers[handIndex];

        cardNumbers[handIndex] = -1;

        // 사람 플레이어의 교환 연출 중에는 이전 족보를 잠시 숨깁니다.
        if (IsHumanPlayer)
        {
            HideHandRank();
        }

        return oldCardNumber;
    }

    /// <summary>
    /// 카드 번호가 현재 손패의 몇 번째 자리에 있는지 반환합니다.
    /// 없으면 -1을 반환합니다.
    /// </summary>
    public int FindHandIndex(
        int cardNumber)
    {
        return cardNumbers.IndexOf(cardNumber);
    }

    /// <summary>
    /// 카드가 이동할 손패 RectTransform을 반환합니다.
    /// </summary>
    public RectTransform GetCardPosition(
        int handIndex)
    {
        if (cardPositions == null ||
            handIndex < 0 ||
            handIndex >= cardPositions.Length)
        {
            return null;
        }

        return cardPositions[handIndex];
    }

    /// <summary>
    /// 지정한 손패 인덱스가 현재 교환 대상으로 선택되어 있는지 반환합니다.
    /// CardControl의 선택 카드 상승 연출에서 사용합니다.
    /// </summary>
    public bool IsExchangeCardSelected(
        int handIndex)
    {
        if (handIndex < 0 ||
            handIndex >= cardNumbers.Count)
        {
            return false;
        }

        return selectedExchangeIndexes.Contains(
            handIndex
        );
    }

    public void ToggleExchangeCard(
        int handIndex)
    {
        // 실제 카드의 IPointerClickHandler와 기존 UI Button OnClick이
        // 같은 클릭으로 동시에 실행되는 경우 두 번째 요청은 무시합니다.
        if (lastExchangeToggleFrame == Time.frameCount &&
            lastExchangeToggleHandIndex == handIndex)
        {
            return;
        }

        lastExchangeToggleFrame = Time.frameCount;
        lastExchangeToggleHandIndex = handIndex;

        if (gameManager == null)
        {
            return;
        }

        // 최초 카드 5장을 받은 뒤부터 자신의 교환이 끝나기 전까지만
        // 추천 선택을 수정할 수 있습니다. 교환 완료 후에는 다시 선택해도
        // 사용할 곳이 없으므로 카드 클릭과 선택 상승을 모두 잠급니다.
        if (!IsHumanPlayer ||
            isFolded ||
            hasExchangedThisGame ||
            !HasValidFiveCardHand() ||
            gameManager.IsDealingCards ||
            gameManager.CurrentPhase == GamePhase.Showdown ||
            gameManager.IsPlayerCurrentlyExchanging(playerNumber))
        {
            return;
        }

        if (handIndex < 0 ||
            handIndex >= cardNumbers.Count)
        {
            return;
        }

        if (selectedExchangeIndexes.Contains(
                handIndex))
        {
            selectedExchangeIndexes.Remove(
                handIndex
            );

            PlayExchangeCardSelectionSound(false);

            gameManager.NotifyHumanExchangeSelectionChanged(
                this
            );

            return;
        }

        if (selectedExchangeIndexes.Count >=
            gameManager.MaxExchangeCards)
        {
            string message =
                "카드는 최대 " +
                gameManager.MaxExchangeCards +
                "장까지만 교환할 수 있습니다.";

            Debug.Log(message);
            gameManager.ShowErrorAlarm(message);

            return;
        }

        selectedExchangeIndexes.Add(
            handIndex
        );

        PlayExchangeCardSelectionSound(true);

        gameManager.NotifyHumanExchangeSelectionChanged(
            this
        );
    }

    /// <summary>
    /// 교환 카드의 수동 선택/선택 해제 효과음을 재생합니다.
    /// 자동 추천으로 선택값을 넣을 때는 호출하지 않습니다.
    /// </summary>
    private void PlayExchangeCardSelectionSound(
        bool isSelected)
    {
        PokerAudioManager manager =
            ResolveExchangeSelectionAudioManager();

        if (manager == null)
        {
            if (!hasLoggedMissingExchangeAudioManager)
            {
                hasLoggedMissingExchangeAudioManager = true;

                Debug.LogWarning(
                    "PlayerControl " +
                    playerNumber +
                    ": 카드 교환 선택 효과음을 재생할 PokerAudioManager를 찾지 못했습니다. " +
                    "Exchange Selection Audio Manager 또는 GameManager의 Audio Manager를 연결해주세요."
                );
            }

            return;
        }

        hasLoggedMissingExchangeAudioManager = false;

        if (isSelected)
        {
            manager.PlayCardExchangeSelect();
        }
        else
        {
            manager.PlayCardExchangeCancel();
        }
    }

    /// <summary>
    /// 교환 선택 효과음에 사용할 PokerAudioManager를 안전하게 찾습니다.
    /// PlayerControl 직접 연결 → GameManager Audio Manager → 같은 오브젝트 → 씬 검색 순서입니다.
    /// </summary>
    private PokerAudioManager ResolveExchangeSelectionAudioManager()
    {
        if (exchangeSelectionAudioManager != null)
        {
            return exchangeSelectionAudioManager;
        }

        if (gameManager != null)
        {
            if (gameManager.audioManager != null)
            {
                exchangeSelectionAudioManager =
                    gameManager.audioManager;

                return exchangeSelectionAudioManager;
            }

            exchangeSelectionAudioManager =
                gameManager.GetComponent<PokerAudioManager>();

            if (exchangeSelectionAudioManager != null)
            {
                return exchangeSelectionAudioManager;
            }
        }

        exchangeSelectionAudioManager =
            UnityEngine.Object.FindFirstObjectByType<PokerAudioManager>(
                FindObjectsInactive.Include
            );

        return exchangeSelectionAudioManager;
    }

    /// <summary>
    /// 카드 재정렬이나 상태 변경 이후 범위를 벗어난 선택 인덱스를 제거합니다.
    /// </summary>
    private void RemoveInvalidCardSelections()
    {
        for (int i = selectedExchangeIndexes.Count - 1;
             i >= 0;
             i--)
        {
            int index = selectedExchangeIndexes[i];

            if (index < 0 ||
                index >= cardNumbers.Count ||
                !CardUtility.IsValidCardNumber(cardNumbers[index]))
            {
                selectedExchangeIndexes.RemoveAt(i);
            }
        }
    }

    /// <summary>
    /// 게임매니저가 최초 손패에서 계산한 추천 교환 인덱스를 한 번에 적용합니다.
    /// 자동 선택 후 자신의 교환이 끝나기 전까지만 카드 클릭으로 수정할 수 있습니다.
    /// </summary>
    public void SetExchangeSelection(
        IList<int> handIndexes)
    {
        selectedExchangeIndexes.Clear();

        // 교환을 이미 완료한 뒤에는 추천이나 수동 선택을 다시 만들지 않습니다.
        if (hasExchangedThisGame)
        {
            lastExchangeToggleFrame = -1;
            lastExchangeToggleHandIndex = -1;

            if (gameManager != null)
            {
                gameManager.NotifyHumanExchangeSelectionChanged(
                    this
                );
            }

            return;
        }

        if (handIndexes != null)
        {
            int maxSelectionCount =
                gameManager != null
                    ? gameManager.MaxExchangeCards
                    : 3;

            if (maxSelectionCount <= 0)
            {
                maxSelectionCount = 0;
            }

            for (int i = 0;
                 i < handIndexes.Count &&
                 selectedExchangeIndexes.Count < maxSelectionCount;
                 i++)
            {
                int handIndex = handIndexes[i];

                if (handIndex < 0 ||
                    handIndex >= cardNumbers.Count ||
                    selectedExchangeIndexes.Contains(handIndex))
                {
                    continue;
                }

                selectedExchangeIndexes.Add(handIndex);

                if (selectedExchangeIndexes.Count >=
                    maxSelectionCount)
                {
                    break;
                }
            }
        }

        selectedExchangeIndexes.Sort();

        // 자동 적용 직후 첫 사용자 클릭이 중복 클릭 방지에 걸리지 않도록 초기화합니다.
        lastExchangeToggleFrame = -1;
        lastExchangeToggleHandIndex = -1;

        if (gameManager != null)
        {
            gameManager.NotifyHumanExchangeSelectionChanged(
                this
            );
        }
    }

    public void ClearExchangeSelectionButton()
    {
        SetExchangeSelection(null);
    }

    public void ConfirmExchangeButton()
    {
        if (gameManager == null)
        {
            return;
        }

        gameManager.SubmitExchange(
            this,
            selectedExchangeIndexes
        );
    }

    public void CompleteExchange(
        int exchangeCount)
    {
        exchangedCardCount =
            Mathf.Clamp(
                exchangeCount,
                0,
                5
            );

        hasExchangedThisGame = true;

        selectedExchangeIndexes.Clear();

        RefreshExchangeCountUI();

        // 사람 플레이어는 교환이 완전히 끝난 뒤 새 족보를 다시 표시합니다.
        if (IsHumanPlayer && !isFolded)
        {
            ShowCurrentHandRank();
        }
    }

    #endregion

    #region Hand Rank UI

    /// <summary>
    /// 현재 손패가 정상적인 카드 5장으로 구성되어 있는지 확인합니다.
    /// 교환 중 빈 슬롯은 -1이므로 유효한 족보로 계산하지 않습니다.
    /// </summary>
    public bool HasValidFiveCardHand()
    {
        if (cardNumbers == null || cardNumbers.Count != 5)
        {
            return false;
        }

        for (int i = 0; i < cardNumbers.Count; i++)
        {
            if (!CardUtility.IsValidCardNumber(cardNumbers[i]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// 현재 카드 5장을 판정하여 족보박스에 표시합니다.
    /// </summary>
    public void ShowCurrentHandRank()
    {
        if (isFolded)
        {
            ShowFoldedHandRank();
            return;
        }

        if (!HasValidFiveCardHand())
        {
            HideHandRank();
            return;
        }

        PokerHandValue handValue =
            PokerHandEvaluator.Evaluate(cardNumbers);

        string displayText =
            CreateHandRankDisplayText(
                handValue
            );

        SetHandRankUI(true, displayText);
        RefreshHandOutlines(handValue);
    }

    /// <summary>
    /// 현재 족보를 실제로 구성하는 카드의 아웃라인만 켭니다.
    /// 플레이어 0은 최초 분배 및 교환 완료 후 호출되고,
    /// AI 플레이어는 쇼다운에서 족보 공개 시 호출됩니다.
    /// </summary>
    private void RefreshHandOutlines(
        PokerHandValue handValue)
    {
        ClearHandOutlines();

        if (handValue == null ||
            isFolded ||
            !HasValidFiveCardHand())
        {
            return;
        }

        HashSet<int> targetCardNumbers =
            GetOutlineTargetCardNumbers(handValue);

        if (targetCardNumbers.Count == 0)
        {
            return;
        }

        CardControl[] allCardControls =
            FindObjectsOfType<CardControl>(true);

        for (int i = 0;
             i < allCardControls.Length;
             i++)
        {
            CardControl cardControl =
                allCardControls[i];

            if (cardControl == null ||
                !targetCardNumbers.Contains(
                    cardControl.CardNumber))
            {
                continue;
            }

            cardControl.SetHandOutline(true);

            if (!outlinedCardNumbers.Contains(
                    cardControl.CardNumber))
            {
                outlinedCardNumbers.Add(
                    cardControl.CardNumber
                );
            }
        }
    }

    /// <summary>
    /// 족보 종류에 따라 강조할 실제 카드 번호를 계산합니다.
    /// 하이카드는 가장 높은 카드 1장, 페어류는 해당 숫자의 카드,
    /// 스트레이트·플러쉬·풀하우스·스트레이트 플러쉬는 5장 모두입니다.
    /// </summary>
    private HashSet<int> GetOutlineTargetCardNumbers(
        PokerHandValue handValue)
    {
        HashSet<int> result =
            new HashSet<int>();

        if (handValue == null ||
            !HasValidFiveCardHand())
        {
            return result;
        }

        switch (handValue.Category)
        {
            case PokerHandCategory.Straight:
            case PokerHandCategory.Flush:
            case PokerHandCategory.FullHouse:
            case PokerHandCategory.StraightFlush:
                AddAllCurrentCards(result);
                break;

            case PokerHandCategory.HighCard:
            case PokerHandCategory.OnePair:
            case PokerHandCategory.ThreeOfAKind:
            case PokerHandCategory.FourOfAKind:
                AddCardsWithRank(
                    result,
                    GetTieBreakerValue(
                        handValue.TieBreakers,
                        0
                    )
                );
                break;

            case PokerHandCategory.TwoPair:
                AddCardsWithRank(
                    result,
                    GetTieBreakerValue(
                        handValue.TieBreakers,
                        0
                    )
                );

                AddCardsWithRank(
                    result,
                    GetTieBreakerValue(
                        handValue.TieBreakers,
                        1
                    )
                );
                break;
        }

        return result;
    }

    private void AddAllCurrentCards(
        HashSet<int> result)
    {
        if (result == null)
        {
            return;
        }

        for (int i = 0; i < cardNumbers.Count; i++)
        {
            int cardNumber = cardNumbers[i];

            if (CardUtility.IsValidCardNumber(cardNumber))
            {
                result.Add(cardNumber);
            }
        }
    }

    private void AddCardsWithRank(
        HashSet<int> result,
        int targetRank)
    {
        if (result == null || targetRank < 2)
        {
            return;
        }

        for (int i = 0; i < cardNumbers.Count; i++)
        {
            int cardNumber = cardNumbers[i];

            if (!CardUtility.IsValidCardNumber(cardNumber))
            {
                continue;
            }

            int rank =
                (int)CardUtility.GetRank(cardNumber) + 2;

            if (rank == targetRank)
            {
                result.Add(cardNumber);
            }
        }
    }

    private int GetTieBreakerValue(
        List<int> tieBreakers,
        int index)
    {
        if (tieBreakers == null ||
            index < 0 ||
            index >= tieBreakers.Count)
        {
            return -1;
        }

        return tieBreakers[index];
    }

    /// <summary>
    /// 이 플레이어가 이전에 켜 둔 아웃라인만 끕니다.
    /// 다른 플레이어의 쇼다운 아웃라인에는 영향을 주지 않습니다.
    /// </summary>
    public void ClearHandOutlines()
    {
        if (outlinedCardNumbers.Count == 0)
        {
            return;
        }

        CardControl[] allCardControls =
            FindObjectsOfType<CardControl>(true);

        for (int i = 0;
             i < allCardControls.Length;
             i++)
        {
            CardControl cardControl =
                allCardControls[i];

            if (cardControl == null ||
                !outlinedCardNumbers.Contains(
                    cardControl.CardNumber))
            {
                continue;
            }

            cardControl.SetHandOutline(false);
        }

        outlinedCardNumbers.Clear();
    }

    /// <summary>
    /// 족보 이름과 대표 숫자를 게임용 문구로 만듭니다.
    /// 키커 전체는 표시하지 않고 족보를 이해하는 데 필요한 대표 랭크만 표시합니다.
    /// </summary>
    private string CreateHandRankDisplayText(
        PokerHandValue handValue)
    {
        if (handValue == null)
        {
            return string.Empty;
        }

        List<int> tieBreakers =
            handValue.TieBreakers;

        switch (handValue.Category)
        {
            case PokerHandCategory.HighCard:
                return "하이카드 [" +
                       GetTieBreakerRank(tieBreakers, 0) +
                       "]";

            case PokerHandCategory.OnePair:
                return "원페어 [" +
                       GetTieBreakerRank(tieBreakers, 0) +
                       "]";

            case PokerHandCategory.TwoPair:
                return "투페어 [" +
                       GetTieBreakerRank(tieBreakers, 0) +
                       "," +
                       GetTieBreakerRank(tieBreakers, 1) +
                       "]";

            case PokerHandCategory.ThreeOfAKind:
                return "트리플 [" +
                       GetTieBreakerRank(tieBreakers, 0) +
                       "]";

            case PokerHandCategory.Straight:
                return "스트레이트 [" +
                       GetFiveCardRanksInHandOrder() +
                       "]";

            case PokerHandCategory.Flush:
                return "플러쉬 [" +
                       GetCurrentHandSuitMark() +
                       "]";

            case PokerHandCategory.FullHouse:
                return "풀하우스 [" +
                       GetTieBreakerRank(tieBreakers, 0) +
                       "," +
                       GetTieBreakerRank(tieBreakers, 1) +
                       "]";

            case PokerHandCategory.FourOfAKind:
                return "포카드 [" +
                       GetTieBreakerRank(tieBreakers, 0) +
                       "]";

            case PokerHandCategory.StraightFlush:
                return "스트레이트 플러쉬 [" +
                       GetFiveCardRanksInHandOrder() +
                       "]";

            default:
                return PokerHandEvaluator.GetCategoryName(
                    handValue.Category
                );
        }
    }

    /// <summary>
    /// TieBreaker에서 지정한 위치의 랭크를 화면용 문자열로 반환합니다.
    /// 값이 없을 경우 빈 문자열을 반환합니다.
    /// </summary>
    private string GetTieBreakerRank(
        List<int> tieBreakers,
        int index)
    {
        if (tieBreakers == null ||
            index < 0 ||
            index >= tieBreakers.Count)
        {
            return string.Empty;
        }

        return GetRankDisplayName(
            tieBreakers[index]
        );
    }

    /// <summary>
    /// 현재 손패 5장의 숫자를 cardNumbers에 들어 있는 순서대로
    /// 쉼표 뒤 공백 없이 반환합니다.
    /// </summary>
    private string GetFiveCardRanksInHandOrder()
    {
        if (!HasValidFiveCardHand())
        {
            return string.Empty;
        }

        StringBuilder builder =
            new StringBuilder();

        for (int i = 0; i < cardNumbers.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(',');
            }

            int rank =
                (int)CardUtility.GetRank(
                    cardNumbers[i]
                ) + 2;

            builder.Append(
                GetRankDisplayName(rank)
            );
        }

        return builder.ToString();
    }

    /// <summary>
    /// 현재 손패의 무늬를 ♣, ♦, ♥, ♠ 중 하나로 반환합니다.
    /// 플러쉬가 아닌 손패에서는 호출하지 않습니다.
    /// </summary>
    private string GetCurrentHandSuitMark()
    {
        if (!HasValidFiveCardHand())
        {
            return string.Empty;
        }

        CardSuit suit =
            CardUtility.GetSuit(cardNumbers[0]);

        switch (suit)
        {
            case CardSuit.Club:
                return "♣";

            case CardSuit.Diamond:
                return "♦";

            case CardSuit.Heart:
                return "♥";

            case CardSuit.Spade:
                return "♠";

            default:
                return string.Empty;
        }
    }

    /// <summary>
    /// 족보 계산에서 사용하는 2~14 랭크를 화면 표시용 문자로 변환합니다.
    /// </summary>
    private string GetRankDisplayName(
        int rank)
    {
        switch (rank)
        {
            case 14:
                return "A";

            case 13:
                return "K";

            case 12:
                return "Q";

            case 11:
                return "J";

            default:
                return rank.ToString();
        }
    }

    /// <summary>
    /// 쇼다운 또는 단독 승리로 게임이 끝났을 때 호출합니다.
    /// 다이한 플레이어는 다이, 생존 플레이어는 실제 족보를 표시합니다.
    /// </summary>
    public void ShowHandRankAtGameEnd()
    {
        if (isFolded)
        {
            ShowFoldedHandRank();
            return;
        }

        ShowCurrentHandRank();
    }

    public void ShowFoldedHandRank()
    {
        ClearHandOutlines();
        SetHandRankUI(true, "다이");
    }

    public void HideHandRank()
    {
        ClearHandOutlines();
        SetHandRankUI(false, string.Empty);
    }

    private void SetHandRankUI(
        bool visible,
        string displayText)
    {
        desiredHandRankVisible = visible;
        desiredHandRankText = displayText ?? string.Empty;

        SetGameObjectActiveIfNeeded(
            handRankBox,
            desiredHandRankVisible
        );

        if (handRankText != null)
        {
            handRankText.text = desiredHandRankText;
        }
    }

    #endregion

    #region UI

    public void RefreshAllUI()
    {
        RefreshCurrentMoneyUI();
        RefreshRoundBetMoneyUI();
        RefreshExchangeCountUI();
        RefreshAllInUI();

        if (turnIndicatorObject != null)
        {
            turnIndicatorObject.SetActive(
                isCurrentTurn
            );
        }

        if (indicatorBack != null)
        {
            indicatorBack.SetActive(
                isCurrentTurn
            );
        }
    }

    private void RefreshAllInUI()
    {
        SetGameObjectActiveIfNeeded(
            allInObject,
            hasGoneAllInThisGame
        );
    }

    private void RestoreCriticalUIVisibility()
    {
        SetGameObjectActiveIfNeeded(
            handRankBox,
            desiredHandRankVisible
        );

        if (handRankText != null &&
            handRankText.text != desiredHandRankText)
        {
            handRankText.text = desiredHandRankText;
        }

        ApplyDesiredResultVisibility();
        RefreshAllInUI();
    }

    private static void SetGameObjectActiveIfNeeded(
        GameObject target,
        bool active)
    {
        if (target != null && target.activeSelf != active)
        {
            target.SetActive(active);
        }
    }

    private void RefreshCurrentMoneyUI()
    {
        if (currentMoneyText == null)
        {
            return;
        }

        currentMoneyText.text =
            FormatKoreanMoney(currentMoney);
    }

    private void RefreshRoundBetMoneyUI()
    {
        if (roundBetMoneyText == null)
        {
            return;
        }

        roundBetMoneyText.text =
            "베팅 " +
            FormatKoreanMoney(
                roundBetMoney
            );
    }

    private void RefreshExchangeCountUI()
    {
        if (exchangeCountText == null)
        {
            return;
        }

        if (!hasExchangedThisGame)
        {
            exchangeCountText.text = "";
            return;
        }

        if (exchangedCardCount == 0)
        {
            exchangeCountText.text =
                "교환 안함";
        }
        else
        {
            exchangeCountText.text =
                "교환 " +
                exchangedCardCount +
                "장";
        }
    }

    public static string FormatKoreanMoney(
        long amount)
    {
        if (amount == 0L)
        {
            return "0";
        }

        bool isNegative = amount < 0L;

        ulong absoluteAmount;

        if (isNegative)
        {
            absoluteAmount =
                (ulong)(-(amount + 1L)) +
                1UL;
        }
        else
        {
            absoluteAmount =
                (ulong)amount;
        }

        ulong eok =
            absoluteAmount /
            100_000_000UL;

        ulong man =
            (absoluteAmount %
             100_000_000UL) /
            10_000UL;

        ulong remainder =
            absoluteAmount %
            10_000UL;

        StringBuilder builder =
            new StringBuilder();

        if (isNegative)
        {
            builder.Append("-");
        }

        if (eok > 0UL)
        {
            builder.Append(eok);
            builder.Append("억");
        }

        if (man > 0UL)
        {
            AppendSpaceIfNeeded(builder);

            builder.Append(man);
            builder.Append("만");
        }

        if (remainder > 0UL)
        {
            AppendSpaceIfNeeded(builder);
            builder.Append(remainder);
        }

        return builder.ToString();
    }

    /// <summary>
    /// 테이블의 팟/콜 금액용 표시입니다.
    /// 억, 만, 1만 미만 칩 단위를 공백으로 구분합니다.
    /// 예: 112331234 -> 1억 1233만 1234칩
    /// </summary>
    public static string FormatKoreanChipAmount(
        long amount)
    {
        if (amount == 0L)
        {
            return "0칩";
        }

        bool isNegative = amount < 0L;

        ulong absoluteAmount;

        if (isNegative)
        {
            absoluteAmount =
                (ulong)(-(amount + 1L)) +
                1UL;
        }
        else
        {
            absoluteAmount =
                (ulong)amount;
        }

        ulong eok =
            absoluteAmount /
            100_000_000UL;

        ulong man =
            (absoluteAmount %
             100_000_000UL) /
            10_000UL;

        ulong chips =
            absoluteAmount %
            10_000UL;

        StringBuilder builder =
            new StringBuilder();

        if (isNegative)
        {
            builder.Append("-");
        }

        if (eok > 0UL)
        {
            builder.Append(eok);
            builder.Append("억");
        }

        if (man > 0UL)
        {
            AppendSpaceIfNeeded(builder);
            builder.Append(man);
            builder.Append("만");
        }

        if (chips > 0UL)
        {
            AppendSpaceIfNeeded(builder);
            builder.Append(chips);
        }

        builder.Append("칩");

        return builder.ToString();
    }

    private static void AppendSpaceIfNeeded(
        StringBuilder builder)
    {
        if (builder.Length == 0)
        {
            return;
        }

        if (builder[builder.Length - 1] == '-')
        {
            return;
        }

        builder.Append(" ");
    }

    #endregion

#if UNITY_EDITOR
    private void OnValidate()
    {
        ResolveResultTextReferences();
        ResolvePlayerAnimationReference();

        if (!Application.isPlaying)
        {
            ApplyFoldedVisual(isFolded);
        }

        playerNumber =
            Mathf.Clamp(
                playerNumber,
                0,
                4
            );

        aiAggression = Mathf.Clamp01(aiAggression);
        aiHandSelectivity = Mathf.Clamp01(aiHandSelectivity);
        aiBluffTendency = Mathf.Clamp01(aiBluffTendency);

        if (applyAIStylePresetWhenStyleChanges &&
            aiStyle != PokerAIStyle.Custom &&
            lastAppliedAIStyle != (int)aiStyle)
        {
            ApplyAIStylePreset();
        }
        else if (lastAppliedAIStyle != (int)aiStyle)
        {
            lastAppliedAIStyle = (int)aiStyle;
        }

        startingMoney =
            Math.Max(
                0L,
                startingMoney
            );

        currentMoney =
            Math.Max(
                0L,
                currentMoney
            );

        roundBetMoney =
            Math.Max(
                0L,
                roundBetMoney
            );

        totalBetThisGame =
            Math.Max(
                0L,
                totalBetThisGame
            );

        exchangedCardCount =
            Mathf.Clamp(
                exchangedCardCount,
                0,
                5
            );

        if (cardPositions == null ||
            cardPositions.Length != 5)
        {
            Array.Resize(
                ref cardPositions,
                5
            );
        }

        if (Application.isPlaying)
        {
            RefreshAllUI();
        }
    }
#endif
}