namespace HapticsTutorial
{
    /// <summary>
    /// Which version of the tutorial is running. The no-props version (built by
    /// Haptics Tutorial > Build and Run: Without Props, which defines HAPTICS_NO_PROPS) uses only
    /// virtual cubes: no camera tracking and no real objects, as a comparison condition.
    /// In the editor, Haptics Tutorial > Play Mode Without Props switches Play Mode too.
    /// </summary>
    public static class TutorialMode
    {
        public const string EditorNoPropsKey = "HapticsTutorial.PlayWithoutProps";

        public static bool WithProps
        {
            get
            {
#if HAPTICS_NO_PROPS
                return false;
#elif UNITY_EDITOR
                return !UnityEditor.EditorPrefs.GetBool(EditorNoPropsKey, false);
#else
                return true;
#endif
            }
        }

        /// <summary>Logged with each session so runs of the two versions can be compared.</summary>
        public static string Condition => WithProps ? "props" : "no_props";
    }
}
