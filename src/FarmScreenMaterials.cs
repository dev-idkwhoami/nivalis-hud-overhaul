using Nivalis;
using UnityEngine;

namespace NivalisMods.HudOverhaul;

// Screens are attached after the module has cached its original meshes. Extend
// that native cache so placement overrides and restoration include the TV too.
internal sealed class FarmScreenMaterials
{
    private HoldableEntityRenderer? _owner;
    private MeshRenderer[] _meshes = Array.Empty<MeshRenderer>();

    internal void Bind(HoldableEntity? held, GameObject screen)
    {
        if (held == null) return;
        _owner = held.EntityRenderer;
        if (_owner == null) return;
        _owner.CacheMaterials();
        _meshes = screen.GetComponentsInChildren<MeshRenderer>(true).ToArray();
        foreach (var mesh in _meshes)
        {
            if (!_owner._meshRenderers.Contains(mesh)) _owner._meshRenderers.Add(mesh);
            _owner._meshMaterialDictionary[mesh] = mesh.sharedMaterial;
            if (_owner.HasOverride) mesh.sharedMaterial = _owner._currentMaterialOverride;
        }
    }

    internal void SetDisplay(MeshRenderer display, Material material)
    {
        // Produce can change during a move. Update what native restoration will
        // use without painting the normal icon over the placement material.
        if (_owner != null)
        {
            _owner._meshMaterialDictionary[display] = material;
            if (_owner.HasOverride) return;
        }
        display.sharedMaterial = material;
    }

    internal void Detach()
    {
        if (_owner != null)
            foreach (var mesh in _meshes)
            {
                _owner._meshRenderers.Remove(mesh);
                _owner._standardRenderers.Remove(mesh);
                _owner._meshMaterialDictionary.Remove(mesh);
            }
        _owner = null;
        _meshes = Array.Empty<MeshRenderer>();
    }
}
