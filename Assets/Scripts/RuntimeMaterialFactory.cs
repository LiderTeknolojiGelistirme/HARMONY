using UnityEngine;

public static class RuntimeMaterialFactory
{
    private static readonly string[] OpaqueShaderPriority =
    {
        "Universal Render Pipeline/Lit",
        "Universal Render Pipeline/Unlit",
        "HDRP/Lit",
        "HDRP/Unlit",
        "Standard",
        "Unlit/Color",
        "Sprites/Default"
    };

    private static readonly string[] TransparentShaderPriority =
    {
        "Universal Render Pipeline/Unlit",
        "Universal Render Pipeline/Lit",
        "HDRP/Unlit",
        "HDRP/Lit",
        "Unlit/Color",
        "Sprites/Default",
        "Standard"
    };

    public static Material CreateOpaqueColorMaterial(Color color)
    {
        Shader shader = FindFirstShader(OpaqueShaderPriority);
        if (shader == null)
            return null;

        Material mat = new Material(shader);
        SetColor(mat, color);
        return mat;
    }

    public static Material CreateTransparentColorMaterial(Color color)
    {
        Shader shader = FindFirstShader(TransparentShaderPriority);
        if (shader == null)
            return null;

        Material mat = new Material(shader);
        SetColor(mat, color);
        ConfigureTransparencyIfSupported(mat);
        return mat;
    }

    private static Shader FindFirstShader(string[] shaderNames)
    {
        for (int i = 0; i < shaderNames.Length; i++)
        {
            Shader s = Shader.Find(shaderNames[i]);
            if (s != null)
                return s;
        }

        return null;
    }

    public static void SetColor(Material material, Color color)
    {
        if (material == null)
            return;

        if (material.HasProperty("_BaseColor"))
            material.SetColor("_BaseColor", color);
        if (material.HasProperty("_Color"))
            material.SetColor("_Color", color);
    }

    private static void ConfigureTransparencyIfSupported(Material material)
    {
        if (material == null)
            return;

        // URP
        if (material.HasProperty("_Surface"))
            material.SetFloat("_Surface", 1f);
        if (material.HasProperty("_Blend"))
            material.SetFloat("_Blend", 0f);
        if (material.HasProperty("_SrcBlend"))
            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        if (material.HasProperty("_DstBlend"))
            material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        if (material.HasProperty("_ZWrite"))
            material.SetFloat("_ZWrite", 0f);

        material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
    }
}

