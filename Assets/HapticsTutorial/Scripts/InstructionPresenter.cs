using UnityEngine;
using UnityEngine.UI;

namespace HapticsTutorial
{
    /// <summary>
    /// Short world-space prompts. Keep copy brief so it stays readable in VR (Vidal et al. 2024).
    /// </summary>
    public class InstructionPresenter : MonoBehaviour
    {
        [SerializeField] Text titleText;
        [SerializeField] Text bodyText;
        [SerializeField] Image panel;

        public void Show(string title, string body, Color? panelTint = null)
        {
            if (titleText != null)
                titleText.text = title;
            if (bodyText != null)
                bodyText.text = body;
            if (panel != null && panelTint.HasValue)
                panel.color = panelTint.Value;
        }

        public void SetPanelColor(Color color)
        {
            if (panel != null)
                panel.color = color;
        }
    }
}
