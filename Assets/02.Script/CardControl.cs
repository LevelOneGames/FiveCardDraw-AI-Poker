using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

/// <summary>
/// Canvas 위의 실제 카드 한 장을 제어합니다.
///
/// 카드 번호는 부모 아래의 형제 순서로 자동 지정됩니다.
/// 0번째 자식은 카드 0, 51번째 자식은 카드 51입니다.
///
/// 매 프레임 게임매니저의 플레이어 카드 리스트를 확인하여
/// 사용하지 않은 카드 더미, 교환 카드 더미, 플레이어 손패 중
/// 알맞은 위치로 부드럽게 이동합니다.
/// </summary>
[RequireComponent(typeof(RectTransform))]
[RequireComponent(typeof(Image))]
public class CardControl : MonoBehaviour, IPointerClickHandler
{
    [Header("References")]
    [SerializeField]
    private FiveCardDrawGameManager gameManager;

    [SerializeField]
    private RectTransform cardRectTransform;

    [SerializeField]
    private Image cardImage;

    [Header("Hand Outline")]
    [Tooltip("이 카드가 현재 족보를 구성할 때 켜질 아웃라인 오브젝트입니다.")]
    public GameObject handOutlineObject;

    [Header("Card Color")]
    [Tooltip("평상시 카드 Image 색상입니다.")]
    public Color normalCardColor =
        new Color(1f, 1f, 1f, 1f);

    [Tooltip("쇼다운에서 족보 구성에 포함되지 않은 생존 플레이어 손패 카드의 색상입니다.")]
    public Color showdownUnusedCardColor =
        new Color(0.4f, 0.4f, 0.4f, 1f);

    [Tooltip("다이한 플레이어가 가진 카드의 Image 색상입니다.")]
    public Color foldedCardColor =
        new Color(0.5f, 0.5f, 0.5f, 1f);

    [Header("Runtime Card State")]
    [Tooltip("부모 아래에서의 형제 순서로 0~51이 자동 지정됩니다.")]
    [SerializeField]
    private int cardNumber = -1;

    [Tooltip("아직 플레이어에게 분배되지 않은 카드입니다.")]
    [SerializeField]
    private bool isUnusedCard = true;

    [Tooltip("교환되어 버린 카드 더미로 이동한 카드입니다.")]
    [SerializeField]
    private bool isDiscardedCard;

    [Tooltip("교환을 시작한 뒤 반환용 뒷면을 계속 유지하는 상태입니다.")]
    [SerializeField]
    private bool isReturningToDiscardPile;

    [SerializeField]
    private int ownerPlayerNumber = -1;

    [SerializeField]
    private int ownerHandIndex = -1;

    [Header("Card Selection")]
    [Tooltip("선택된 사용자 카드를 원래 손패 위치보다 위로 올리는 거리입니다.")]
    public float selectedCardYOffset = 50f;

    [Tooltip("선택하거나 선택 해제할 때 올라가고 내려오는 속도입니다. 작을수록 빠릅니다.")]
    [Min(0.01f)]
    public float selectionMoveSmoothTime = 0.05f;

    [Tooltip("선택 이동이 목표에 도착했다고 판단하는 거리입니다.")]
    [Min(0.01f)]
    public float selectionArrivalDistance = 0.5f;

    [Header("Movement")]
    [Tooltip("작을수록 카드가 더 빠르게 목표 위치에 도착합니다.")]
    [Min(0.01f)]
    public float moveSmoothTime = 0.16f;

    [Tooltip("카드 이동 최대 속도입니다.")]
    [Min(0.01f)]
    public float maxMoveSpeed = 5000f;

    [Header("Sibling Sorting")]
    [Tooltip("활성화하면 매 프레임 X좌표가 큰 카드가 더 나중 형제가 되어 UI에서 위에 그려집니다.")]
    public bool sortSiblingByX = true;

    [Header("Scale")]
    [Tooltip("스케일이 목표 크기로 변하는 부드러움입니다.")]
    [Min(0.01f)]
    public float scaleSmoothTime = 0.12f;

    [Tooltip("사용자 손패의 최종 Y 위치입니다.")]
    public float humanHandTargetY = -286f;

    [Tooltip("사용자 손패 최종 위치에서의 카드 크기입니다.")]
    [Min(1f)]
    public float humanHandMaxScale = 1.5f;

    private RectTransform currentTarget;
    private Vector3 moveVelocity;
    private float scaleVelocity;
    private Sprite faceSprite;
    private Sprite lastAppliedSprite;

    // 아웃라인 오브젝트의 연결 여부와 관계없이 현재 족보 포함 상태를 저장합니다.
    // 쇼다운 카드 색상은 이 값을 기준으로 판정합니다.
    private bool isHandOutlineVisible;

    // 카드 선택 상태가 바뀐 동안에는 일반 이동보다 빠른 선택 전용 속도를 사용합니다.
    private bool previousSelectionRaised;
    private bool isSelectionMoveActive;

    // 같은 부모에 있는 카드들은 한 프레임에 한 번만 정렬합니다.
    private static readonly Dictionary<int, int>
        LastSortedFrameByParent = new Dictionary<int, int>();

    // 매 프레임 새로운 배열이나 리스트를 만들지 않기 위한 공용 버퍼입니다.
    private static readonly List<CardControl>
        SiblingCardSortBuffer = new List<CardControl>(52);

    public int CardNumber
    {
        get { return cardNumber; }
    }

    public bool IsUnusedCard
    {
        get { return isUnusedCard; }
    }

    public bool IsDiscardedCard
    {
        get { return isDiscardedCard; }
    }

    public bool IsInPlayerHand
    {
        get
        {
            return !isUnusedCard &&
                   !isDiscardedCard;
        }
    }

    private void Awake()
    {
        EnsureReferencesAndCardNumber();
        SetHandOutline(false);
    }

    /// <summary>
    /// 컴포넌트 참조와 최초 카드번호를 안전하게 준비합니다.
    /// 카드번호는 한 번 지정된 뒤 형제 순서가 바뀌어도 유지됩니다.
    /// </summary>
    private void EnsureReferencesAndCardNumber()
    {
        if (cardRectTransform == null)
        {
            cardRectTransform =
                GetComponent<RectTransform>();
        }

        if (cardImage == null)
        {
            cardImage =
                GetComponent<Image>();
        }

        if (cardNumber < 0)
        {
            // 최초 실행 시점의 부모 자식 순서를 카드 번호로 저장합니다.
            cardNumber =
                transform.GetSiblingIndex();
        }
    }

    private void Start()
    {
        FindGameManagerIfNeeded();
        CacheFaceSprite();
        ValidateCardNumber();
        ResolveCardStateAndTarget();
        ApplyCardSprite(true);
        ApplyCardColor();
    }

    private void Update()
    {
        FindGameManagerIfNeeded();

        if (gameManager == null)
        {
            return;
        }

        if (faceSprite == null)
        {
            CacheFaceSprite();
        }

        ResolveCardStateAndTarget();
        MoveToCurrentTarget();
        UpdateScaleByYPosition();
        ApplyCardSprite(false);
        ApplyCardColor();
    }

    private void LateUpdate()
    {
        FindGameManagerIfNeeded();

        if (gameManager != null)
        {
            // 게임매니저의 교환 코루틴이 Update 이후에 카드 리스트를 바꿔도
            // 같은 프레임의 LateUpdate에서 상태를 다시 확인합니다.
            ResolveCardStateAndTarget();
            UpdateReturnBackFallbackState();

            // 다른 스크립트가 Image.sprite 또는 color를 바꾸더라도 프레임 마지막에
            // 현재 상태에 맞는 카드 앞뒷면과 쇼다운 색상을 다시 적용합니다.
            ApplyCardSprite(true);
            ApplyCardColor();
        }

        SortSiblingCardsByXOncePerFrame();
    }

    /// <summary>
    /// 사용자 카드가 눌렸을 때 교환 선택 상태를 토글합니다.
    /// 실제 선택 가능 여부는 게임매니저와 PlayerControl에서 검사합니다.
    /// </summary>
    public void OnPointerClick(PointerEventData eventData)
    {
        FindGameManagerIfNeeded();

        if (gameManager == null)
        {
            return;
        }

        gameManager.ToggleExchangeSelectionByCardNumber(
            cardNumber
        );
    }

    /// <summary>
    /// 이 카드가 현재 족보를 구성하는 카드인지 표시합니다.
    /// 아웃라인은 카드의 자식 오브젝트로 두면 카드 이동과 함께 따라갑니다.
    /// </summary>
    public void SetHandOutline(bool visible)
    {
        isHandOutlineVisible = visible;

        if (handOutlineObject != null)
        {
            handOutlineObject.SetActive(visible);
        }

        // 쇼다운 도중 아웃라인 상태가 바뀌면 같은 프레임에 카드 색상도 갱신합니다.
        ApplyCardColor();
    }

    /// <summary>
    /// 기존 호출부와의 호환을 위한 초기화 함수입니다.
    /// 실제 처리는 즉시 초기화 함수에서 수행합니다.
    /// </summary>
    public void ResetCardState()
    {
        ResetCardStateImmediate();
    }

    /// <summary>
    /// 새 게임 또는 게임 리셋 시 카드를 사용 전 상태로 즉시 초기화합니다.
    /// 이동 애니메이션 없이 분배 전 카드 더미로 순간 이동하며,
    /// 스케일 1과 기본 카드 뒷면을 즉시 적용합니다.
    /// </summary>
    public void ResetCardStateImmediate()
    {
        EnsureReferencesAndCardNumber();
        FindGameManagerIfNeeded();

        isUnusedCard = true;
        isDiscardedCard = false;
        isReturningToDiscardPile = false;
        ownerPlayerNumber = -1;
        ownerHandIndex = -1;

        // 새 게임에서는 전판 족보 강조와 카드 선택 이동 상태를 즉시 제거합니다.
        SetHandOutline(false);
        previousSelectionRaised = false;
        isSelectionMoveActive = false;

        moveVelocity = Vector3.zero;
        scaleVelocity = 0f;

        if (cardRectTransform != null)
        {
            cardRectTransform.localScale = Vector3.one;
        }

        // 새 게임에서는 쇼다운 및 다이 어둡기 효과도 즉시 제거합니다.
        if (cardImage != null)
        {
            cardImage.color = normalCardColor;
        }

        if (gameManager != null)
        {
            currentTarget =
                gameManager.unusedCardDeckPosition;

            SnapImmediatelyToTarget(currentTarget);

            if (cardImage != null &&
                gameManager.cardBackSprite != null)
            {
                cardImage.sprite =
                    gameManager.cardBackSprite;

                lastAppliedSprite =
                    gameManager.cardBackSprite;
            }
            else
            {
                lastAppliedSprite = null;
            }
        }
        else
        {
            currentTarget = null;
            lastAppliedSprite = null;
        }
    }

    /// <summary>
    /// 지정 위치로 이동 보간 없이 즉시 이동합니다.
    /// </summary>
    private void SnapImmediatelyToTarget(
        RectTransform target)
    {
        if (cardRectTransform == null ||
            target == null ||
            cardRectTransform.parent == null)
        {
            return;
        }

        Vector3 targetLocalPosition =
            cardRectTransform.parent.InverseTransformPoint(
                target.position
            );

        targetLocalPosition.z =
            cardRectTransform.localPosition.z;

        cardRectTransform.localPosition =
            targetLocalPosition;
    }

    private void FindGameManagerIfNeeded()
    {
        if (gameManager != null)
        {
            return;
        }

        gameManager =
            FindObjectOfType<FiveCardDrawGameManager>();
    }

    private void CacheFaceSprite()
    {
        if (gameManager == null)
        {
            return;
        }

        faceSprite =
            gameManager.GetCardFaceSprite(
                cardNumber
            );
    }

    private void ValidateCardNumber()
    {
        if (CardUtility.IsValidCardNumber(cardNumber))
        {
            return;
        }

        Debug.LogError(
            name +
            ": 카드 번호가 0~51 범위를 벗어났습니다. " +
            "카드 오브젝트 52개만 같은 부모 아래에 순서대로 배치했는지 확인하세요. " +
            "현재 번호: " +
            cardNumber
        );
    }

    private void ResolveCardStateAndTarget()
    {
        RectTransform handTarget;
        int foundPlayerNumber;
        int foundHandIndex;

        bool isInHand =
            gameManager.TryGetCardTarget(
                cardNumber,
                out handTarget,
                out foundPlayerNumber,
                out foundHandIndex
            );

        if (isInHand)
        {
            isUnusedCard = false;
            isDiscardedCard = false;
            isReturningToDiscardPile = false;

            ownerPlayerNumber =
                foundPlayerNumber;

            ownerHandIndex =
                foundHandIndex;

            currentTarget = handTarget;
            return;
        }

        ownerPlayerNumber = -1;
        ownerHandIndex = -1;

        if (gameManager.IsCardDiscarded(cardNumber))
        {
            isUnusedCard = false;
            isDiscardedCard = true;
            isReturningToDiscardPile = true;
            currentTarget =
                gameManager.discardedCardPosition;
        }
        else
        {
            isUnusedCard = true;
            isDiscardedCard = false;
            currentTarget =
                gameManager.unusedCardDeckPosition;

            // 새 게임 준비 중에는 지난 판의 반환 상태를 해제합니다.
            // 교환 페이즈 중에는 아래 보조 판정에서 반환 상태를 유지할 수 있습니다.
            if (gameManager.CurrentPhase != GamePhase.Exchange)
            {
                isReturningToDiscardPile = false;
            }
        }
    }

    /// <summary>
    /// 교환 카드 목록 갱신이 늦거나 누락된 경우를 대비한 보조 판정입니다.
    /// 교환 페이즈 중 손패 리스트에서는 빠졌지만 아직 사용자 영역(Y &lt; 0)에 있는 카드는
    /// 사용자가 방금 버린 카드로 판단하고 반환용 뒷면 상태를 고정합니다.
    /// </summary>
    private void UpdateReturnBackFallbackState()
    {
        if (gameManager == null)
        {
            return;
        }

        if (isDiscardedCard ||
            currentTarget == gameManager.discardedCardPosition)
        {
            isReturningToDiscardPile = true;
            return;
        }

        if (gameManager.CurrentPhase != GamePhase.Exchange)
        {
            return;
        }

        bool isNotInAnyPlayerHand =
            ownerPlayerNumber < 0 &&
            ownerHandIndex < 0;

        bool isStillInsideHumanHandArea =
            GetCurrentAnchoredY() < 0f;

        if (isNotInAnyPlayerHand &&
            isStillInsideHumanHandArea)
        {
            // 한 번 반환 카드로 감지되면 교환 더미에 도착할 때까지
            // Y값이 0 이상이 되어도 반환용 뒷면을 계속 유지합니다.
            isReturningToDiscardPile = true;
        }
    }

    private void MoveToCurrentTarget()
    {
        if (cardRectTransform == null ||
            currentTarget == null ||
            cardRectTransform.parent == null)
        {
            return;
        }

        Vector3 targetLocalPosition =
            cardRectTransform.parent.InverseTransformPoint(
                currentTarget.position
            );

        bool shouldRaise =
            ShouldRaiseForExchangeSelection();

        // 선택 상태가 바뀌는 순간부터 목표 위치에 도착할 때까지
        // 선택 전용의 더 빠른 이동 속도를 사용합니다.
        if (shouldRaise != previousSelectionRaised)
        {
            previousSelectionRaised = shouldRaise;
            isSelectionMoveActive = true;
            moveVelocity = Vector3.zero;
        }

        if (shouldRaise)
        {
            targetLocalPosition.y += selectedCardYOffset;
        }

        // UI 카드의 기존 Z값을 유지합니다.
        targetLocalPosition.z =
            cardRectTransform.localPosition.z;

        float activeSmoothTime =
            isSelectionMoveActive
                ? selectionMoveSmoothTime
                : moveSmoothTime;

        if (activeSmoothTime <= 0.01f)
        {
            cardRectTransform.localPosition =
                targetLocalPosition;

            moveVelocity = Vector3.zero;
            isSelectionMoveActive = false;
            return;
        }

        cardRectTransform.localPosition =
            Vector3.SmoothDamp(
                cardRectTransform.localPosition,
                targetLocalPosition,
                ref moveVelocity,
                activeSmoothTime,
                maxMoveSpeed,
                Time.deltaTime
            );

        if (isSelectionMoveActive)
        {
            float remainingDistance =
                Vector3.Distance(
                    cardRectTransform.localPosition,
                    targetLocalPosition
                );

            if (remainingDistance <=
                selectionArrivalDistance)
            {
                cardRectTransform.localPosition =
                    targetLocalPosition;

                moveVelocity = Vector3.zero;
                isSelectionMoveActive = false;
            }
        }
    }

    /// <summary>
    /// 현재 카드가 사용자 선택 카드인지 확인합니다.
    /// 손패 5장을 받은 뒤부터 쇼다운 전까지 선택할 수 있으며,
    /// 실제 교환 애니메이션 중에는 새 카드가 같은 인덱스를 사용해도 올라가지 않습니다.
    /// </summary>
    private bool ShouldRaiseForExchangeSelection()
    {
        if (gameManager == null ||
            ownerPlayerNumber != 0 ||
            ownerHandIndex < 0 ||
            isUnusedCard ||
            isDiscardedCard)
        {
            return false;
        }

        return gameManager.IsCardSelectedForExchange(
            cardNumber
        );
    }

    private void UpdateScaleByYPosition()
    {
        if (cardRectTransform == null)
        {
            return;
        }

        float currentY =
            GetCurrentAnchoredY();

        float scaleProgress = 0f;

        if (currentY < 0f)
        {
            float targetY =
                Mathf.Min(-0.01f, humanHandTargetY);

            scaleProgress =
                Mathf.InverseLerp(
                    0f,
                    targetY,
                    currentY
                );
        }

        float targetScale =
            Mathf.Lerp(
                1f,
                humanHandMaxScale,
                scaleProgress
            );

        float currentScale =
            cardRectTransform.localScale.x;

        float nextScale;

        if (scaleSmoothTime <= 0.01f)
        {
            nextScale = targetScale;
            scaleVelocity = 0f;
        }
        else
        {
            nextScale =
                Mathf.SmoothDamp(
                    currentScale,
                    targetScale,
                    ref scaleVelocity,
                    scaleSmoothTime,
                    Mathf.Infinity,
                    Time.deltaTime
                );
        }

        cardRectTransform.localScale =
            new Vector3(
                nextScale,
                nextScale,
                1f
            );
    }

    /// <summary>
    /// 같은 부모 아래의 카드들을 현재 X좌표 기준으로 정렬합니다.
    /// X가 작은 카드는 앞쪽 형제, X가 큰 카드는 뒤쪽 형제가 됩니다.
    /// Unity UI에서는 뒤쪽 형제가 위에 그려지므로 오른쪽 카드가 위로 올라옵니다.
    /// </summary>
    private void SortSiblingCardsByXOncePerFrame()
    {
        if (!sortSiblingByX ||
            transform.parent == null)
        {
            return;
        }

        Transform cardParent = transform.parent;
        int parentInstanceId =
            cardParent.GetInstanceID();

        int lastSortedFrame;

        if (LastSortedFrameByParent.TryGetValue(
                parentInstanceId,
                out lastSortedFrame) &&
            lastSortedFrame == Time.frameCount)
        {
            return;
        }

        LastSortedFrameByParent[parentInstanceId] =
            Time.frameCount;

        SiblingCardSortBuffer.Clear();

        for (int i = 0;
             i < cardParent.childCount;
             i++)
        {
            Transform child =
                cardParent.GetChild(i);

            CardControl siblingCard =
                child.GetComponent<CardControl>();

            if (siblingCard == null ||
                !siblingCard.gameObject.activeInHierarchy)
            {
                continue;
            }

            SiblingCardSortBuffer.Add(siblingCard);
        }

        SiblingCardSortBuffer.Sort(
            CompareCardsByCurrentX
        );

        for (int i = 0;
             i < SiblingCardSortBuffer.Count;
             i++)
        {
            SiblingCardSortBuffer[i]
                .transform
                .SetSiblingIndex(i);
        }
    }

    private static int CompareCardsByCurrentX(
        CardControl left,
        CardControl right)
    {
        float leftX =
            left.GetCurrentAnchoredX();

        float rightX =
            right.GetCurrentAnchoredX();

        int xCompare =
            leftX.CompareTo(rightX);

        if (xCompare != 0)
        {
            return xCompare;
        }

        // 카드가 같은 위치에 겹쳐 있을 때는 카드번호로 순서를 고정해
        // 매 프레임 형제 순서가 흔들리지 않도록 합니다.
        return left.cardNumber.CompareTo(
            right.cardNumber
        );
    }

    private float GetCurrentAnchoredX()
    {
        if (cardRectTransform != null)
        {
            return cardRectTransform
                .anchoredPosition.x;
        }

        return transform.localPosition.x;
    }

    /// <summary>
    /// RectTransform 인스펙터에 표시되는 Anchored Position Y를 반환합니다.
    /// 사용자 손패 위치 -286과 앞면/뒷면 경계 0을 같은 기준으로 판정합니다.
    /// </summary>
    private float GetCurrentAnchoredY()
    {
        if (cardRectTransform != null)
        {
            return cardRectTransform.anchoredPosition.y;
        }

        return transform.localPosition.y;
    }

    private void ApplyCardSprite(
        bool force)
    {
        if (cardImage == null ||
            gameManager == null)
        {
            return;
        }

        float currentY =
            GetCurrentAnchoredY();

        Sprite targetSprite;

        bool isOwnerFolded =
            IsOwnerPlayerFolded();

        // 쇼다운에서는 생존 플레이어의 손패만 공개합니다.
        // 다이한 플레이어 손패는 사람 카드가 화면 아래에 있어도 반드시 뒷면을 유지합니다.
        bool shouldRevealAtShowdown =
            gameManager.CurrentPhase == GamePhase.Showdown &&
            IsInPlayerHand &&
            !isOwnerFolded;

        bool shouldKeepFoldedCardBackAtShowdown =
            gameManager.CurrentPhase == GamePhase.Showdown &&
            IsInPlayerHand &&
            isOwnerFolded;

        bool shouldShowReturnBack =
            isDiscardedCard ||
            isReturningToDiscardPile ||
            currentTarget == gameManager.discardedCardPosition;

        if (shouldKeepFoldedCardBackAtShowdown)
        {
            targetSprite = gameManager.cardBackSprite;
        }
        else if (shouldRevealAtShowdown)
        {
            targetSprite = faceSprite;
        }
        else if (shouldShowReturnBack)
        {
            // 버린 카드로 감지된 순간부터 위치와 관계없이 반환용 뒷면입니다.
            // LateUpdate에서도 매 프레임 강제로 적용하므로 이동 시작 즉시 바뀝니다.
            targetSprite =
                gameManager.cardReturnBackSprite != null
                    ? gameManager.cardReturnBackSprite
                    : gameManager.cardBackSprite;
        }
        else if (isUnusedCard)
        {
            // 새 게임 초기화 직후에는 이전 위치의 Y값과 관계없이
            // 사용 전 카드이므로 항상 기본 카드 뒷면을 표시합니다.
            targetSprite =
                gameManager.cardBackSprite;
        }
        else if (currentY < 0f)
        {
            // Player 0의 손패 영역에서는 카드 앞면을 보여줍니다.
            targetSprite = faceSprite;
        }
        else
        {
            // AI 손패에는 기본 카드 뒷면을 보여줍니다.
            targetSprite =
                gameManager.cardBackSprite;
        }

        if (targetSprite == null)
        {
            return;
        }

        if (!force &&
            lastAppliedSprite == targetSprite)
        {
            return;
        }

        cardImage.sprite = targetSprite;
        lastAppliedSprite = targetSprite;
    }

    private bool IsOwnerPlayerFolded()
    {
        if (gameManager == null ||
            ownerPlayerNumber < 0)
        {
            return false;
        }

        PlayerControl ownerPlayer =
            gameManager.GetPlayerByNumber(
                ownerPlayerNumber
            );

        return ownerPlayer != null &&
               ownerPlayer.IsFolded;
    }

    /// <summary>
    /// 다이한 플레이어의 카드는 게임 도중부터 foldedCardColor로 표시합니다.
    /// 생존 플레이어는 기존처럼 쇼다운에서 족보에 포함되지 않은 카드만 더 어둡게 표시합니다.
    /// </summary>
    private void ApplyCardColor()
    {
        if (cardImage == null)
        {
            return;
        }

        bool isOwnerFolded =
            IsInPlayerHand &&
            IsOwnerPlayerFolded();

        bool shouldDimAtShowdown =
            !isOwnerFolded &&
            gameManager != null &&
            gameManager.CurrentPhase == GamePhase.Showdown &&
            IsInPlayerHand &&
            !isHandOutlineVisible;

        Color targetColor;

        if (isOwnerFolded)
        {
            targetColor = foldedCardColor;
        }
        else if (shouldDimAtShowdown)
        {
            targetColor = showdownUnusedCardColor;
        }
        else
        {
            targetColor = normalCardColor;
        }

        if (cardImage.color != targetColor)
        {
            cardImage.color = targetColor;
        }

        // PlayerControl에서도 다이 시 아웃라인을 지우지만,
        // 프레임 순서와 관계없이 카드 쪽에서도 한 번 더 강제로 차단합니다.
        if (isOwnerFolded && isHandOutlineVisible)
        {
            isHandOutlineVisible = false;

            if (handOutlineObject != null &&
                handOutlineObject.activeSelf)
            {
                handOutlineObject.SetActive(false);
            }
        }
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        selectionMoveSmoothTime =
            Mathf.Max(0.01f, selectionMoveSmoothTime);

        selectionArrivalDistance =
            Mathf.Max(0.01f, selectionArrivalDistance);

        moveSmoothTime =
            Mathf.Max(0.01f, moveSmoothTime);

        maxMoveSpeed =
            Mathf.Max(0.01f, maxMoveSpeed);

        scaleSmoothTime =
            Mathf.Max(0.01f, scaleSmoothTime);

        humanHandMaxScale =
            Mathf.Max(1f, humanHandMaxScale);
    }
#endif
}