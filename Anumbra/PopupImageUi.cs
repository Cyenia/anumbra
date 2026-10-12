using System;
using System.IO;
using System.Numerics;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;

namespace Anumbra;

public sealed class PopupImageUi() : Window("Preview Image", ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
{
    private IDalamudTextureWrap? _textureProvider;
    private float _frameHeight = ImGui.GetFrameHeight();
    private float _itemSpacingHeight = ImGui.GetStyle().ItemSpacing.Y;
    
    public async Task Show(IDalamudTextureWrap? textureProvider, string filename)
    {
        if (textureProvider == null || filename == string.Empty) return;

        _textureProvider = await Plugin.TextureProvider.CreateFromExistingTextureAsync(textureProvider);
        
        var displaySize = ImGui.GetIO().DisplaySize / 2;
        var scale    = ImGuiHelpers.GlobalScale;
        var maxW     = Math.Min(_textureProvider.Width, displaySize.X * scale);
        var maxH     = Math.Min(_textureProvider.Height, displaySize.Y * scale);
        var fitScale = Math.Min(Math.Min(maxW / _textureProvider.Width, maxH / _textureProvider.Height), 1f);
        
        var width = _textureProvider.Width * fitScale;
        var height = _textureProvider.Height * fitScale;
        var windowHeaderHeight = (_itemSpacingHeight * 2);
        windowHeaderHeight += width > height ? _frameHeight : 0;
        
        Size = new Vector2(width, height + windowHeaderHeight);
        SizeCondition = ImGuiCond.Always;
        WindowName = $"Anumbra Preview - {Path.GetFileName(filename)}";
        IsOpen = true;
    }
    
    public override void Draw()
    {
        if (_textureProvider == null || Size == null) return;
        if (_textureProvider.Handle.IsNull) return;
        if(SizeCondition == ImGuiCond.Always)
            SizeCondition = ImGuiCond.Appearing;
        
        var avail = ImGui.GetContentRegionAvail();
        
        var fitScale = Math.Min(Math.Min(avail.X / _textureProvider.Width, avail.Y / _textureProvider.Height), 1f);
        var width = _textureProvider.Width * fitScale;
        var height = _textureProvider.Height * fitScale;

        var cursorPosX = (avail.X / 2f) - (width / 2f) + (_itemSpacingHeight * 2f);
        var cursorPosY = (avail.Y / 2f) - (height / 2f) + _frameHeight + (_itemSpacingHeight * 2f);
        ImGui.SetCursorPos(new Vector2(cursorPosX, cursorPosY));

        ImGui.Image(_textureProvider.Handle, new Vector2(width, height));
    }
}
