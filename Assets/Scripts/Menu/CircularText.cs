using UnityEngine;
using TMPro;

[ExecuteAlways] // Pour voir l'effet dans l'éditeur sans lancer le jeu
public class CircularText : MonoBehaviour
{
    // PARAMÈTRES RÉGLABLES DANS L'INSPECTOR
    [Tooltip("Rayon du cercle sur lequel le texte sera placé.")]
    public float radius = 100f;
    [Tooltip("Décalage de l'angle de départ (en degrés).")]
    public float angleOffset = 0f;
    [Tooltip("Angle total sur lequel le texte s'étend (360 pour un cercle complet).")]
    [Range(0f, 360f)]
    public float arcAngle = 360f;

    private TMP_Text m_TextComponent;
    private bool m_isDirty = false;

    void Awake()
    {
        m_TextComponent = GetComponent<TMP_Text>();
    }

    // Le script est mis à jour chaque fois que le texte ou les paramètres changent
    void OnEnable() { TMPro_EventManager.TEXT_CHANGED_EVENT.Add(OnTextChanged); }
    void OnDisable() { TMPro_EventManager.TEXT_CHANGED_EVENT.Remove(OnTextChanged); }
    void OnValidate() { m_isDirty = true; } // Pour l'édition dans l'Inspector

    void Update()
    {
        if (m_TextComponent == null)
        {
            m_TextComponent = GetComponent<TMP_Text>();
            if (m_TextComponent == null) return;
        }

        if (m_TextComponent.IsActive() && (m_TextComponent.havePropertiesChanged || m_isDirty))
        {
            // Force la reconstruction du maillage
            m_TextComponent.ForceMeshUpdate();
            // Applique l'effet
            WarpText();
            m_TextComponent.havePropertiesChanged = false;
            m_isDirty = false;
        }
    }

    void OnTextChanged(Object obj)
    {
        if (obj == m_TextComponent)
        {
            m_isDirty = true;
        }
    }

    // FONCTION PRINCIPALE POUR APPLIQUER L'EFFET
    private void WarpText()
    {
        if (m_TextComponent == null || m_TextComponent.textInfo.characterCount == 0 || radius == 0)
            return;

        // Informations nécessaires pour manipuler le maillage
        TMP_TextInfo textInfo = m_TextComponent.textInfo;
        int characterCount = textInfo.characterCount;

        // Calcul de l'angle total et de l'incrément par caractère
        // On utilise la largeur préférée pour calculer la distribution
        float charactersWidth = m_TextComponent.preferredWidth;
        float anglePerUnit = arcAngle / charactersWidth;

        // Parcours de tous les caractères (Glyphes)
        for (int i = 0; i < characterCount; i++)
        {
            if (!textInfo.characterInfo[i].isVisible)
                continue;

            // 1. OBTENIR LES INFORMATIONS
            int materialIndex = textInfo.characterInfo[i].materialReferenceIndex;
            int vertexIndex = textInfo.characterInfo[i].vertexIndex;

            // Centre du caractère (point autour duquel on va tourner)
            Vector3[] sourceVertices = textInfo.meshInfo[materialIndex].vertices;
            Vector3 center = (sourceVertices[vertexIndex + 0] + sourceVertices[vertexIndex + 2]) / 2f;

            // 2. CALCULER LA POSITION ET L'ANGLE
            // Angle du caractère sur le cercle
            float angle = angleOffset - (center.x * anglePerUnit);
            float radians = angle * Mathf.Deg2Rad;

            // Position (X, Y) sur le cercle
            Vector3 newPos = new Vector3(Mathf.Cos(radians), Mathf.Sin(radians), 0) * radius;

            // Angle de base (tangent à l'arc)
            float baseRotationAngle = angle + 90f;

            // 3. CORRECTION DE LISIBILITÉ (CLÉ !)
            // Si l'angle se situe dans la moitié inférieure du cercle (entre 90° et 270°),
            // on inverse la rotation pour que le texte soit à l'endroit.
            float normalizedAngle = (angle % 360 + 360) % 360; // Assure que l'angle est positif [0, 360]

            baseRotationAngle -= 180f;


            // 4. CRÉER ET APPLIQUER LA MATRICE DE TRANSFORMATION
            Quaternion rotation = Quaternion.Euler(0, 0, baseRotationAngle);
            Matrix4x4 matrix = Matrix4x4.TRS(newPos, rotation, Vector3.one);

            for (int j = 0; j < 4; j++)
            {
                Vector3 vertex = sourceVertices[vertexIndex + j];
                sourceVertices[vertexIndex + j] = matrix.MultiplyPoint(vertex - center);
            }
        }

        // 5. ENVOYER LE MAILLAGE MIS À JOUR À UNITY
        m_TextComponent.UpdateVertexData(TMP_VertexDataUpdateFlags.Vertices);
    }
}
