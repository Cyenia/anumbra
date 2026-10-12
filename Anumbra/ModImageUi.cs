using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;

namespace Anumbra;

public sealed class ModImageUi : IDisposable
{
    private readonly Configuration _config;

    private string       _modPath = string.Empty;
    private List<string> _images  = [];
    private int          _index;

    private readonly Dictionary<string, byte[]> _renameBuffers = new();
    private const int BufSize = 256;

    private static readonly string[] SupportedExtensions = [".webp", ".png", ".jpg", ".jpeg", ".bmp"];

    private const float MaxW = 500f;
    private const float MaxH = 200f;

    public ModImageUi(Configuration config)
    {
        _config = config;
    }

    // ── carousel: above the tab bar ──────────────────────────────────────────
    // Kept intentionally simple (only flat ImGui calls — no tree nodes) so it
    // cannot disrupt Penumbra's header layout state.

    public void DrawCarousel(string modPath)
    {
        Sync(modPath);

        ImGui.Separator();

        if (_images.Count == 0)
        {
            ImGui.Text("No preview images.");
            ImGui.SameLine();
            if (ImGui.Button("Add Image##AnumbraCarouselAdd"))
                OpenAddDialog(modPath);
        }
        else
        {
            // nav + add row — rendered BEFORE the image so it is never
            // pushed below the fold by Penumbra's height-capped child window.
            var avail = ImGui.GetContentRegionAvail();
            if (ImGui.Button("Add Image##AnumbraCarouselAdd"))
                OpenAddDialog(modPath);

            var wrap = Plugin.TextureProvider
                             .GetFromFile(_images[_index])
                             .GetWrapOrDefault();

            ImGui.SameLine();

            if (_images.Count > 1)
            {
                var text = $"{_index + 1} / {_images.Count}   {Path.GetFileName(_images[_index])}";
                if (wrap != null)
                {
                    var buttonSize = ImGuiHelpers.GetButtonSize(FontAwesomeIcon.ArrowLeft.ToIconString());
                    var textSize = ImGui.CalcTextSize(text);
                    var totalWidth = (buttonSize.X * 2f) + textSize.X + (ImGui.GetStyle().ItemSpacing.X * 3f);
                    if (totalWidth < avail.X)
                        ImGui.SetCursorPosX((avail.X / 2f) - (totalWidth / 2f) + ImGui.GetStyle().ItemSpacing.X);
                }

                if (ImGui.ArrowButton("##AnumbraPrev", ImGuiDir.Left))
                    _index = (_index - 1 + _images.Count) % _images.Count;
                ImGui.SameLine();
                ImGui.Text(text);
                ImGui.SameLine();
                if (ImGui.ArrowButton("##AnumbraNext", ImGuiDir.Right))
                    _index = (_index + 1) % _images.Count;
            }

            if (wrap != null)
            {
                var scale    = ImGuiHelpers.GlobalScale;
                var maxW     = Math.Min(avail.X, MaxW * scale);
                var maxH     = MaxH * scale;
                var fitScale = Math.Min(Math.Min(maxW / wrap.Width, maxH / wrap.Height), 1f);
                var width = wrap.Width * fitScale;

                ImGui.SetCursorPosX((avail.X / 2f) - (width / 2f) + ImGui.GetStyle().ItemSpacing.X);
                ImGui.Image(wrap.Handle, new Vector2(width, wrap.Height * fitScale));
                if (ImGui.IsItemClicked())
                {
                    _ = Plugin.PopupImageUi.Show(wrap, _images[_index]);
                }
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
                    ImGui.BeginTooltip();
                    ImGui.TextUnformatted("Open preview in new window");
                    ImGui.EndTooltip();
                }
            }
        }

        ImGui.Separator();
    }

    // ── settings tab section: heliosphere-style TreeNode ─────────────────────
    // Injected via PostEnabledDraw — fires right after the Enabled checkbox,
    // before all mod options, so it appears at the very top of the Settings tab.
    // Right-click the section header to access per-plugin settings.

    // Mirror of heliosphere's PenumbraWindowIntegration.PostEnabledDraw
    public void DrawPenumbraSection(string modPath)
    {
        Sync(modPath);

        ImRaii.PushId("anumbra-penumbra-integration");
        ImGui.Spacing();

        using var group = ImRaii.Group();
        if(!group.Alive)
            return;

        var flags = _config.ExpandByDefault
            ? ImGuiTreeNodeFlags.DefaultOpen
            : ImGuiTreeNodeFlags.None;

        using var node = ImRaii.TreeNode("Anumbra", flags);
        if (!node.Success)
            return;

        if (ImGui.Button("Add Preview Images##AnumbraAdd"))
            OpenAddDialog(modPath);

        if (_images.Count == 0)
        {
            ImGui.SameLine();
            ImGui.TextDisabled("(no images yet)");
        }
        else
        {
            ImGui.Spacing();
            for (var i = 0; i < _images.Count; i++)
            {
                var path = _images[i];

                if (!_renameBuffers.TryGetValue(path, out var buf))
                {
                    buf = new byte[BufSize];
                    Encoding.UTF8.GetBytes(Path.GetFileNameWithoutExtension(path), buf);
                    _renameBuffers[path] = buf;
                }

                ImGui.PushItemWidth(150f * ImGuiHelpers.GlobalScale);
                var inputLabel = Encoding.UTF8.GetBytes($"##AnumbraRename{i}\0");
                ImGui.InputText(inputLabel, buf);
                ImGui.PopItemWidth();

                if (ImGui.IsItemDeactivatedAfterEdit())
                    CommitRename(i, Encoding.UTF8.GetString(buf).TrimEnd('\0'));

                ImGui.SameLine();
                ImGui.SetCursorPosX(ImGui.GetCursorPosX() - ImGui.GetStyle().ItemSpacing.X + 2f);
                ImGui.Text(Path.GetExtension(path));

                ImGui.SameLine();
                var deleteLabel = Encoding.UTF8.GetBytes($"Delete##AnumbraDelete{i}\0");
                if (ImGui.Button(deleteLabel))
                {
                    try   { File.Delete(path); }
                    catch (Exception e) { Plugin.Log.Warning($"[Anumbra] Delete failed: {e.Message}"); }
                    _renameBuffers.Remove(path);
                    _images.RemoveAt(i);
                    if (_index >= _images.Count) _index = Math.Max(0, _images.Count - 1);
                    i--;
                }
            }
        }
    }

    // ── file import ──────────────────────────────────────────────────────────

    private void OpenAddDialog(string modPath)
    {
        var imagesDir = Path.Combine(modPath, "images");
        try   { Directory.CreateDirectory(imagesDir); }
        catch (Exception e)
        {
            Plugin.Log.Warning($"[Anumbra] Could not create images dir: {e.Message}");
            return;
        }

        Plugin.FileDialogManager.OpenFileDialog(
            "Select Preview Images",
            "Images{.png,.jpg,.jpeg,.webp,.bmp}",
            (ok, selected) =>
            {
                if (!ok) return;
                foreach (var src in selected)
                    CopyImage(src, imagesDir);
                _modPath = string.Empty;
            },
            selectionCountMax: 0,
            startPath: modPath,
            isModal: false);
    }

    private void CopyImage(string src, string imagesDir)
    {
        try
        {
            File.Copy(src, Path.Combine(imagesDir, Path.GetFileName(src)), overwrite: true);
        }
        catch (Exception e)
        {
            Plugin.Log.Warning($"[Anumbra] Failed to import {src}: {e.Message}");
        }
    }

    // ── rename ────────────────────────────────────────────────────────────────

    private void CommitRename(int i, string newName)
    {
        if (string.IsNullOrWhiteSpace(newName)) return;

        var conflict = _images
            .Where((_, idx) => idx != i)
            .Any(p => string.Equals(Path.GetFileNameWithoutExtension(p), newName,
                                    StringComparison.OrdinalIgnoreCase));
        if (conflict) return;

        var oldPath = _images[i];
        var newPath = Path.Combine(Path.GetDirectoryName(oldPath)!, newName + ".png");
        if (oldPath == newPath) return;

        try
        {
            File.Move(oldPath, newPath);
            var buf = _renameBuffers[oldPath];
            _renameBuffers.Remove(oldPath);
            _renameBuffers[newPath] = buf;
            _images[i] = newPath;
        }
        catch (Exception e)
        {
            Plugin.Log.Warning($"[Anumbra] Rename failed: {e.Message}");
        }
    }

    // ── sync ─────────────────────────────────────────────────────────────────

    private void Sync(string modPath)
    {
        if (modPath == _modPath) return;
        _modPath = modPath;
        _index   = 0;
        _renameBuffers.Clear();
        _images.Clear();

        var dir = Path.Combine(modPath, "images");
        if (!Directory.Exists(dir)) return;

        try
        {
            _images = [.. Directory.GetFiles(dir)
                .Where(f => SupportedExtensions.Contains(Path.GetExtension(f).ToLower()))
                .Order()];
        }
        catch (Exception e)
        {
            Plugin.Log.Warning($"[Anumbra] Could not scan {dir}: {e.Message}");
        }
    }

    public void Dispose() { }
}
