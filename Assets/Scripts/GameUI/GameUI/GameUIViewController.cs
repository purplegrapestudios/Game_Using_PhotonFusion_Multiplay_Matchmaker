using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;

public class GameUIViewController : MonoBehaviour
{
    public static GameUIViewController Instance;
    [SerializeField] private TMP_Text m_healthTxt;
    [SerializeField] private GameObject m_crosshairObject;
    private Crosshair m_crosshair;

    private void Awake()
    {
        Instance = this;
        m_crosshair = m_crosshairObject.GetComponent<Crosshair>();
    }

    public void UpdateHealthText(string value)
    {
        m_healthTxt.text = value;
    }

    public void SetCrosshairActive(bool val)
    {
        m_crosshairObject.SetActive(val);
    }

    public Crosshair GetCrosshair()
    {
        return m_crosshair;
    }
}
