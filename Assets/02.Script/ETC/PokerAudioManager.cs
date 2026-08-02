using System;
using UnityEngine;

/// <summary>
/// 플레이어 행동 음성에 사용할 성별 구분입니다.
/// 각 PlayerControl 인스펙터에서 남자/여자를 선택합니다.
/// </summary>
public enum PlayerVoiceGender
{
    Male = 0,
    Female = 1
}

/// <summary>
/// 다이, 삥, 따당, 콜, 체크, 쿼터, 하프, 올인, 맥스 음성 클립 묶음입니다.
/// </summary>
[Serializable]
public class BettingActionVoiceClipSet
{
    public AudioClip foldClip;
    public AudioClip pingClip;
    public AudioClip doubleClip;
    public AudioClip callClip;
    public AudioClip checkClip;
    public AudioClip quarterClip;
    public AudioClip halfClip;
    public AudioClip allInClip;
    public AudioClip maxClip;

    public AudioClip GetClip(BettingAction action)
    {
        switch (action)
        {
            case BettingAction.Fold:
                return foldClip;

            case BettingAction.Ping:
                return pingClip;

            case BettingAction.Double:
                return doubleClip;

            case BettingAction.Call:
                return callClip;

            case BettingAction.Check:
                return checkClip;

            case BettingAction.Quarter:
                return quarterClip;

            case BettingAction.Half:
                return halfClip;

            case BettingAction.AllIn:
                return allInClip;

            case BettingAction.Max:
                return maxClip;

            default:
                return null;
        }
    }
}

/// <summary>
/// 파이브 카드 드로우에서 사용하는 음성 및 효과음을 한 곳에서 관리합니다.
/// AudioSource를 직접 연결하지 않으면 런타임에 2D AudioSource를 자동 생성합니다.
/// </summary>
[DisallowMultipleComponent]
public class PokerAudioManager : MonoBehaviour
{
    [Header("Player Voice Clips")]
    [Tooltip("남자 플레이어가 행동할 때 사용할 음성 9종입니다.")]
    public BettingActionVoiceClipSet maleActionVoices =
        new BettingActionVoiceClipSet();

    [Tooltip("여자 플레이어가 행동할 때 사용할 음성 9종입니다.")]
    public BettingActionVoiceClipSet femaleActionVoices =
        new BettingActionVoiceClipSet();

    [Header("Card Clips")]
    [Tooltip("게임 시작 시 카드 분배가 시작될 때 한 번 재생하는 분위기/시작 효과음입니다.")]
    public AudioClip gameStartDealingClip;

    [Tooltip("처음 카드를 한 장 분배할 때마다 재생하는 효과음입니다.")]
    public AudioClip cardDealOneClip;

    [Tooltip("카드 한 장을 교환할 때마다 재생하는 효과음입니다.")]
    public AudioClip cardExchangeOneClip;

    [Tooltip("사용자가 교환할 카드를 새로 선택할 때 재생하는 효과음입니다.")]
    public AudioClip cardExchangeSelectClip;

    [Tooltip("사용자가 선택한 교환 카드를 다시 취소할 때 재생하는 효과음입니다.")]
    public AudioClip cardExchangeCancelClip;


    [Header("UI Clips")]
    [Tooltip("버튼 및 토글을 클릭할 때 재생하는 공통 UI 효과음입니다.")]
    public AudioClip uiClickClip;
    [Header("Result And Chip Clips")]
    [Tooltip("게임 종료 후 위너 표시가 나타날 때 재생하는 효과음입니다.")]
    public AudioClip winnerClip;

    [Tooltip("플레이어가 베팅 칩을 테이블로 던질 때 재생하는 효과음입니다.")]
    public AudioClip chipThrowClip;

    [Tooltip("테이블 위 칩이 승자에게 모일 때 재생하는 효과음입니다.")]
    public AudioClip chipCollectClip;

    [Header("Audio Sources")]
    [Tooltip("행동 음성 전용 AudioSource입니다. 비워두면 자동 생성합니다.")]
    public AudioSource voiceSource;

    [Tooltip("카드 분배/교환 전용 AudioSource입니다. 비워두면 자동 생성합니다.")]
    public AudioSource cardSource;

    [Tooltip("칩 투척/회수 전용 AudioSource입니다. 비워두면 자동 생성합니다.")]
    public AudioSource chipSource;

    [Tooltip("게임 시작 및 승리 효과음 전용 AudioSource입니다. 비워두면 자동 생성합니다.")]
    public AudioSource resultSource;


    [Tooltip("버튼 및 토글 클릭 효과음 전용 AudioSource입니다. 비워두면 자동 생성합니다.")]
    public AudioSource uiSource;
    [Header("Volume")]
    [Range(0f, 1f)] public float voiceVolume = 1f;
    [Range(0f, 1f)] public float cardVolume = 1f;
    [Range(0f, 1f)] public float chipVolume = 1f;
    [Range(0f, 1f)] public float resultVolume = 1f;


    [Range(0f, 1f)] public float uiVolume = 1f;
    [Header("Repeated Sound Variation")]
    [Tooltip("연속 카드 소리가 너무 기계적으로 들리지 않도록 피치를 조금 변화시킵니다.")]
    public Vector2 cardPitchRange = new Vector2(0.96f, 1.04f);

    [Tooltip("연속 칩 소리가 너무 기계적으로 들리지 않도록 피치를 조금 변화시킵니다.")]
    public Vector2 chipPitchRange = new Vector2(0.97f, 1.03f);

    private void Awake()
    {
        voiceSource = ResolveOrCreateSource(
            voiceSource,
            "Voice Audio Source"
        );

        cardSource = ResolveOrCreateSource(
            cardSource,
            "Card Audio Source"
        );

        chipSource = ResolveOrCreateSource(
            chipSource,
            "Chip Audio Source"
        );

        resultSource = ResolveOrCreateSource(
            resultSource,
            "Result Audio Source"
        );

        uiSource = ResolveOrCreateSource(
            uiSource,
            "UI Audio Source"
        );
    }

    public void PlayPlayerAction(
        PlayerControl player,
        BettingAction action)
    {
        if (player == null)
        {
            return;
        }

        BettingActionVoiceClipSet set =
            player.voiceGender == PlayerVoiceGender.Female
                ? femaleActionVoices
                : maleActionVoices;

        AudioClip clip = set != null
            ? set.GetClip(action)
            : null;

        PlayVoiceClip(clip);
    }

    public void PlayGameStartDealing()
    {
        PlayOneShot(
            resultSource,
            gameStartDealingClip,
            resultVolume,
            1f
        );
    }

    public void PlayCardDealOne()
    {
        PlayOneShot(
            cardSource,
            cardDealOneClip,
            cardVolume,
            GetRandomPitch(cardPitchRange)
        );
    }

    public void PlayCardExchangeOne()
    {
        PlayOneShot(
            cardSource,
            cardExchangeOneClip,
            cardVolume,
            GetRandomPitch(cardPitchRange)
        );
    }

    /// <summary>
    /// 사용자가 교환할 카드를 새로 선택했을 때 재생합니다.
    /// </summary>
    public void PlayCardExchangeSelect()
    {
        PlayOneShot(
            cardSource,
            cardExchangeSelectClip,
            cardVolume,
            1f
        );
    }

    /// <summary>
    /// 사용자가 선택한 교환 카드를 다시 선택 해제했을 때 재생합니다.
    /// </summary>
    public void PlayCardExchangeCancel()
    {
        PlayOneShot(
            cardSource,
            cardExchangeCancelClip,
            cardVolume,
            1f
        );
    }
    /// <summary>
    /// 버튼, 토글 등 UI의 OnClick 이벤트에 직접 연결하는 공통 클릭 효과음 함수입니다.
    /// </summary>
    public void PlayClickSound()
    {
        PlayOneShot(
            uiSource,
            uiClickClip,
            uiVolume,
            1f
        );
    }



    public void PlayWinner()
    {
        PlayOneShot(
            resultSource,
            winnerClip,
            resultVolume,
            1f
        );
    }

    public void PlayChipThrow()
    {
        PlayOneShot(
            chipSource,
            chipThrowClip,
            chipVolume,
            GetRandomPitch(chipPitchRange)
        );
    }

    public void PlayChipCollect()
    {
        PlayOneShot(
            chipSource,
            chipCollectClip,
            chipVolume,
            1f
        );
    }

    private void PlayVoiceClip(AudioClip clip)
    {
        if (voiceSource == null || clip == null)
        {
            return;
        }

        voiceSource.Stop();
        voiceSource.pitch = 1f;
        voiceSource.clip = clip;
        voiceSource.volume = voiceVolume;
        voiceSource.Play();
    }

    private void PlayOneShot(
        AudioSource source,
        AudioClip clip,
        float volume,
        float pitch)
    {
        if (source == null || clip == null)
        {
            return;
        }

        source.pitch = pitch;
        source.PlayOneShot(
            clip,
            Mathf.Clamp01(volume)
        );
    }

    private AudioSource ResolveOrCreateSource(
        AudioSource source,
        string objectName)
    {
        if (source != null)
        {
            ConfigureSource(source);
            return source;
        }

        GameObject sourceObject = new GameObject(objectName);
        sourceObject.transform.SetParent(transform, false);

        AudioSource createdSource =
            sourceObject.AddComponent<AudioSource>();

        ConfigureSource(createdSource);
        return createdSource;
    }

    private void ConfigureSource(AudioSource source)
    {
        if (source == null)
        {
            return;
        }

        source.playOnAwake = false;
        source.loop = false;
        source.spatialBlend = 0f;
    }

    private float GetRandomPitch(Vector2 range)
    {
        float minimum = Mathf.Min(range.x, range.y);
        float maximum = Mathf.Max(range.x, range.y);

        return UnityEngine.Random.Range(
            Mathf.Max(0.1f, minimum),
            Mathf.Max(0.1f, maximum)
        );
    }
}