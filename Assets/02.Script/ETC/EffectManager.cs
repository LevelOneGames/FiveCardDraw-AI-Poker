using UnityEngine;
using System.Collections;

public class EffectManager : MonoBehaviour {




    public float _leftTime;
    WaitForSeconds falseDelay;
    void Awake()
    {
        falseDelay = new WaitForSeconds(_leftTime);
    }

    void OnEnable()
    {

        StopCoroutine("Disable");
        StartCoroutine("Disable");

    }

    IEnumerator Disable()
    {
        yield return falseDelay;
        gameObject.SetActive(false);
    }
    
    
}
