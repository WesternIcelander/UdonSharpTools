
using UdonSharp;
using UnityEngine;
using VRC.SDK3.Components;
using VRC.SDKBase;
using VRC.Udon;
using SiggiUSTools;
using TMPro;

[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class LanguageSelector : UdonSharpBehaviour
{
    [SerializeField] private I18n i18n;
    [SerializeField] private TMP_Text welcomeText;
    [SerializeField] private TMP_Text motdText;
    [SerializeField] private TMP_Text instructionsText;
    [SerializeField] private TMP_Dropdown dropdown;
    private string[] _languages;
    void Start()
    {
        _languages = i18n.GetLanguages();
        string[] languageNames = new string[_languages.Length];
        for (int i = 0; i < _languages.Length; i++)
        {
            languageNames[i] = i18n.GetLanguageName(_languages[i]);
        }

        dropdown.ClearOptions();
        dropdown.AddOptions(languageNames);

        i18n.AddLanguageChangeListener(this);
        OnLanguageChange();
    }

    public void OnLanguageChange()
    {
        welcomeText.text = i18n._("welcome", new[] {"player_name", Networking.LocalPlayer.displayName});
        motdText.text = i18n._("we_hope_you_enjoy_your_visit");
        instructionsText.text = i18n._("select_a_language");

        string currentLanguage = i18n.GetCurrentLanguage();
        for (int i = 0; i < _languages.Length; i++)
        {
            if (_languages[i] == currentLanguage)
            {
                dropdown.SetValueWithoutNotify(i);
                break;
            }
        }
    }

    public void OnDropdownValueChanged()
    {
        int index = dropdown.value;
        if (index < 0 || index >= _languages.Length) return;
        i18n.SetLanguage(_languages[index]);
    }
}
