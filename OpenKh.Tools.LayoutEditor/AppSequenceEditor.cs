using ImGuiNET;
using OpenKh.Kh2;
using OpenKh.Kh2.Extensions;
using OpenKh.Tools.LayoutEditor.Interfaces;
using System.Numerics;
using Microsoft.Xna.Framework.Graphics;
using OpenKh.Tools.Common.CustomImGui;
using OpenKh.Engine.Extensions;
using OpenKh.Engine.MonoGame;
using OpenKh.Engine.Renders;
using static OpenKh.Tools.Common.CustomImGui.ImGuiEx;
using OpenKh.Engine.Renderers;
using System;
using System.Collections.Generic;
using System.Linq;
using OpenKh.Tools.LayoutEditor.Models;
using System.IO;
using OpenKh.Tools.LayoutEditor.Dialogs;
using OpenKh.Tools.LayoutEditor.Controls;

namespace OpenKh.Tools.LayoutEditor
{
    public class AppSequenceEditor : IApp, ISaveBar, ITextureBinder, IDisposable
    {
        private readonly IEditorSettings _settings;
        private readonly Sequence _sequence;
        private readonly Imgd _image;
        private readonly MonoGameImGuiBootstrap _bootStrap;
        private readonly GraphicsDevice _graphics;
        private readonly KingdomShader _shader;
        private readonly MonoSpriteDrawing _drawing;
        private readonly ISpriteTexture _atlasTexture;
        private DebugSequenceRenderer _debugSequenceRenderer;
        private readonly SequenceRenderer _renderer;

        private int _selectedSprite = 0;
        private int _selectedSpriteGroup = 0;
        private int _selectedAnimGroup;
        private int _animationFrameCurrent;
        private int _animationFrameCount;
        private ISpriteTexture _destinationTexture;
        private IntPtr _destinationTextureId;
        private List<SpriteModel> _sprites;
        private List<SpriteGroupModel> _spriteGroups;

        private MySequencer _sequencer;
        int _sequencerSelectedAnimation = 0;
        int _sequencerFirstFrame = 0;

        private bool _isSpriteEditDialogOpen;
        private SpriteEditDialog _spriteEditDialog;
        private string SpriteEditDialogTitle => $"Sprite edit";

        private bool _isSpriteGroupEditDialogOpen;
        private SpriteGroupEditDialog _spriteGroupEditDialog;
        private string SpriteGroupEditDialogTitle => $"Sprite group edit";

        private Sequence.AnimationGroup SelectedAnimationGroup =>
            _debugSequenceRenderer.AnimationGroup;

        public int SelectedAnimGroup
        {
            get => _selectedAnimGroup;
            set
            {
                _selectedAnimGroup = value;
                _animationFrameCurrent = 0;
                _sequencerSelectedAnimation = 0;
                
                var animationGroup = _sequence.AnimationGroups[_selectedAnimGroup];
                _animationFrameCount = SequenceExtensions.GetFrameLength(animationGroup);
                _debugSequenceRenderer.AnimationGroup = animationGroup;
                _sequencer.SelectedAnimationGroupIndex = value;
            }
        }

        public AppSequenceEditor(
            MonoGameImGuiBootstrap bootstrap,
            IEditorSettings settings,
            Sequence sequence,
            Imgd image)
        {
            _settings = settings;
            _sequence = sequence;
            _image = image;

            _bootStrap = bootstrap;
            _graphics = bootstrap.GraphicsDevice;
            _shader = new KingdomShader(bootstrap.Content);
            _drawing = new MonoSpriteDrawing(_graphics, _shader);
            _atlasTexture = _drawing.CreateSpriteTexture(_image);
            _debugSequenceRenderer = new DebugSequenceRenderer();
            _renderer = new SequenceRenderer(_sequence, _drawing, _atlasTexture)
            {
                DebugSequenceRenderer = _debugSequenceRenderer
            };

            _destinationTexture = _drawing.CreateSpriteTexture(1024, 1024);
            _destinationTextureId = this.BindTexture(_destinationTexture);

            _sprites = sequence.Sprites
                .Select(x => AsSpriteModel(x))
                .ToList();
            _spriteGroups = sequence.SpriteGroups
                .Select((_, i) => AsSpriteGroupModel(i))
                .ToList();

            _sequencer = new MySequencer(sequence, _debugSequenceRenderer);
            SelectedAnimGroup = 0;
        }

        public void Menu()
        {
            ForMenu("Edit", () =>
            {
                ForMenuItem("Sprite groups...", () => _isSpriteGroupEditDialogOpen = true);
                ForMenuItem("Sprites...", () => _isSpriteEditDialogOpen = true);
            });
        }

        public bool Run()
        {
            bool dummy = true;
            if (ImGui.BeginPopupModal(SpriteEditDialogTitle, ref dummy,
                ImGuiWindowFlags.Popup | ImGuiWindowFlags.Modal | ImGuiWindowFlags.AlwaysAutoResize))
            {
                _spriteEditDialog.Run();
                ImGui.EndPopup();
            }
            if (ImGui.BeginPopupModal(SpriteGroupEditDialogTitle, ref dummy,
                ImGuiWindowFlags.Popup | ImGuiWindowFlags.Modal | ImGuiWindowFlags.AlwaysAutoResize))
            {
                _spriteGroupEditDialog.Run();
                ImGui.EndPopup();
            }

            const float SpriteListWidthMul = 1f;
            const float SpriteListWidthMax = 192f;
            const float RightWidthMul = 1.5f;
            const float RightWidthMax = 384f;
            const float PreviewWidthMul = 2f;
            const float TotalWidthMul = SpriteListWidthMul + RightWidthMul + PreviewWidthMul;
            var windowSize = ImGui.GetWindowSize().X;
            var spriteListWidth = Math.Min(windowSize / TotalWidthMul * SpriteListWidthMul, SpriteListWidthMax);
            var rightWidth = Math.Min(windowSize / TotalWidthMul * RightWidthMul, RightWidthMax);
            var previewWidth = windowSize - spriteListWidth - rightWidth;

            ForChild("SpriteGroupList", spriteListWidth, 0, true, DrawSpriteGroupList);
            ImGui.SameLine();
            ForChild("Animation", previewWidth, 0, false, DrawAnimation);
            ImGui.SameLine();
            ForChild("Right", rightWidth, 0, true, DrawRight);

            if (_isSpriteGroupEditDialogOpen)
            {
                ImGui.OpenPopup(SpriteGroupEditDialogTitle);
                _isSpriteGroupEditDialogOpen = false;
                _spriteGroupEditDialog = new SpriteGroupEditDialog(
                    _sequence,
                    _spriteGroups,
                    _selectedSpriteGroup,
                    _drawing,
                    _atlasTexture,
                    this, _settings);
            }

            if (_isSpriteEditDialogOpen)
            {
                ImGui.OpenPopup(SpriteEditDialogTitle);
                _isSpriteEditDialogOpen = false;
                _spriteEditDialog = new SpriteEditDialog(
                    _sprites,
                    _selectedSprite,
                    _drawing,
                    _atlasTexture,
                    this, _settings);
            }

            if (!_sequencer.IsPaused)
                _animationFrameCurrent++;
            return true;
        }

        private void DrawSpriteGroupList()
        {
            // Animate only the selected sprite
            //if (_selectedSpriteGroup >= 0)
            //    _spriteGroups[_selectedSpriteGroup].Draw(0, 0);

            for (int i = 0; i < _spriteGroups.Count; i++)
            {
                var sprite = _spriteGroups[i];
                if (ImGui.Selectable($"##spriteGroup{i}",
                    _selectedSpriteGroup == i,
                    ImGuiSelectableFlags.None,
                    new Vector2(0, sprite.Height)))
                    _selectedSpriteGroup = i;

                ImGui.SameLine();
                ImGui.Image(sprite.TextureId, new Vector2(sprite.Width, sprite.Height));
            }
        }

        private void DrawSpriteList()
        {
            // Animate only the selected sprite
            if (_selectedSprite >= 0)
                _sprites[_selectedSprite].Draw(0, 0);

            for (int i = 0; i < _sprites.Count; i++)
            {
                var sprite = _sprites[i];
                if (ImGui.Selectable($"##sprite{i}",
                    _selectedSprite == i,
                    ImGuiSelectableFlags.None,
                    new Vector2(0, sprite.Height)))
                    _selectedSprite = i;

                ImGui.SameLine();
                ImGui.Image(sprite.TextureId, new Vector2(sprite.Width, sprite.Height));
            }
        }

        private void DrawAnimationGroupList()
        {
            for (int i = 0; i < _sequence.AnimationGroups.Count; i++)
            {
                if (ImGui.Selectable($"Animation Group {i}\n",
                    SelectedAnimGroup == i))
                    SelectedAnimGroup = i;
            }
        }

        private unsafe void DrawAnimation()
        {
            const float TimelineControlWidth = 200f;

            AnimationGroupSelector();

            if (ImGui.BeginChild("timeline", new Vector2(0, TimelineControlWidth)))
            {
                Timeline();
                ImGui.EndChild();
            }

            ForChild("Preview", 0, 0, false, () =>
            {
                const float ViewportWidth = 1024f;
                const float ViewportHeight = 1024f;
                const float Infinite = 65536f;
                var width = ImGui.GetWindowContentRegionWidth();
                var height = ImGui.GetWindowHeight();
                var originX = width / 4.0f;
                var originY = height / 4.0f;
                var backgroundColorInverse = new ColorF(
                    1f - _settings.EditorBackground.R,
                    1f - _settings.EditorBackground.G,
                    1f - _settings.EditorBackground.B,
                    1f);

                _drawing.DestinationTexture = _destinationTexture;
                _drawing.Clear(_settings.EditorBackground);
                _drawing.SetViewport(0, ViewportWidth, 0, ViewportHeight);

                // This draws the origin
                _drawing.FillRectangle(originX - 1, 0, 1, Infinite, backgroundColorInverse);
                _drawing.FillRectangle(0, originY - 1, Infinite, 1, backgroundColorInverse);
                _drawing.Flush();

                _renderer.Draw(_selectedAnimGroup, _animationFrameCurrent, originX, originY);
                _drawing.Flush();
                _drawing.DestinationTexture = null;

                float maxU = 1f / ViewportWidth * width;
                float maxV = 1f / ViewportHeight * height;
                ImGui.Image(_destinationTextureId, new Vector2(width, height),
                    GetUv(_atlasTexture, 0, 0), new Vector2(maxU, maxV));
            });
        }

        private void DrawRight()
        {
            var animationGroup = _sequence.AnimationGroups[_selectedAnimGroup];
            AnimationGroupEdit(animationGroup);

            for (var i = 0; i < animationGroup.Animations.Count; i++)
            {
                if (ImGui.CollapsingHeader($"Animation {i + 1}"))
                {
                    AnimationEdit(animationGroup.Animations[i], i);
                }
            }
        }

        public Bar.Entry SaveAnimation(string name)
        {
            var stream = new MemoryStream();
            _sequence.Write(stream);

            return new Bar.Entry
            {
                Name = name,
                Stream = stream,
                Type = Bar.EntryType.Seqd
            };
        }

        public Bar.Entry SaveTexture(string name)
        {
            var stream = new MemoryStream();
            _image.Write(stream);

            return new Bar.Entry
            {
                Name = name,
                Stream = stream,
                Type = Bar.EntryType.Imgd
            };
        }

        public void Dispose()
        {
            foreach (var sprite in _sprites)
                sprite.Dispose();

            _bootStrap.UnbindTexture(_destinationTextureId);
            _destinationTexture.Dispose();
        }

        private void AnimationGroupEdit(Sequence.AnimationGroup animationGroup)
        {
            var doNotLoop = animationGroup.DoNotLoop != 0;
            if (ImGui.Checkbox("Do not loop", ref doNotLoop))
                animationGroup.DoNotLoop = (short)(doNotLoop ? 1 : 0);

            int unknown06 = animationGroup.Unknown06;
            if (ImGui.InputInt("Unknown06", ref unknown06))
                animationGroup.Unknown06 = (short)unknown06;

            var loopPair = new int[] { animationGroup.LoopStart, animationGroup.LoopEnd };
            if (ImGui.InputInt2("Loop frame", ref loopPair[0]))
            {
                animationGroup.LoopStart = loopPair[0];
                animationGroup.LoopEnd = loopPair[1];
            }

            int unknown10 = animationGroup.Unknown10;
            if (ImGui.InputInt("Unknown10", ref unknown10))
                animationGroup.Unknown10 = unknown10;

            int unknown14 = animationGroup.Unknown14;
            if (ImGui.InputInt("Unknown14", ref unknown14))
                animationGroup.Unknown14 = unknown14;

            int unknown18 = animationGroup.Unknown18;
            if (ImGui.InputInt("Unknown18", ref unknown18))
                animationGroup.Unknown18 = unknown18;

            int unknown1c = animationGroup.Unknown1C;
            if (ImGui.InputInt("Unknown1c", ref unknown1c))
                animationGroup.Unknown1C = unknown1c;

            int unknown20 = animationGroup.Unknown20;
            if (ImGui.InputInt("Unknown20", ref unknown20))
                animationGroup.Unknown20 = unknown20;
        }

        private void AnimationEdit(Sequence.Animation animation, int index)
        {
            int flags = animation.Flags;
            if (ImGui.InputInt($"Flags##{index}", ref flags))
                animation.Flags = flags;

            ImGuiFlagBox(animation, $"Use cubic interpolation instead of linear##{index}", Sequence.LinearInterpolationFlag);

            var framePair = new int[] { animation.FrameStart, animation.FrameEnd };
            if (ImGui.DragInt2($"Frame lifespan##{index}", ref framePair[0]))
            {
                animation.FrameStart = framePair[0];
                animation.FrameEnd = framePair[1];
            }

            var xaPair = new int[] { animation.TranslateXStart, animation.TranslateXEnd };
            if (ImGui.DragInt2($"Translation X##{index}", ref xaPair[0]))
            {
                animation.TranslateXStart = xaPair[0];
                animation.TranslateXEnd = xaPair[1];
            }

            var yaPair = new int[] { animation.TranslateYStart, animation.TranslateYEnd };
            if (ImGui.DragInt2($"Translation Y##{index}", ref yaPair[0]))
            {
                animation.TranslateYStart = yaPair[0];
                animation.TranslateYEnd = yaPair[1];
            }

            if (ImGuiFlagBox(animation, $"Enable pivot translation##{index}", Sequence.PivotFlag))
            {
                var xbPair = new int[] { animation.PivotXStart, animation.PivotXEnd };
                if (ImGui.DragInt2($"Pivot X##{index}", ref xbPair[0]))
                {
                    animation.PivotXStart = xbPair[0];
                    animation.PivotXEnd = xbPair[1];
                }

                var ybPair = new int[] { animation.PivotYStart, animation.PivotYEnd };
                if (ImGui.DragInt2($"Pivot Y##{index}", ref ybPair[0]))
                {
                    animation.PivotYStart = ybPair[0];
                    animation.PivotYEnd = ybPair[1];
                }
            }

            if (ImGuiFlagBox(animation, $"Enable rotation##{index}", Sequence.RotationFlag))
            {
                var rotationStart = new Vector3(
                    (float)(animation.RotationXStart * 180f / Math.PI),
                    (float)(animation.RotationYStart * 180f / Math.PI),
                    (float)(animation.RotationZStart * 180f / Math.PI));
                var rotationEnd = new Vector3(
                    (float)(animation.RotationXEnd * 180f / Math.PI),
                    (float)(animation.RotationYEnd * 180f / Math.PI),
                    (float)(animation.RotationZEnd * 180f / Math.PI));

                if (ImGui.DragFloat3($"Rotation Start##{index}", ref rotationStart))
                {
                    animation.RotationXStart = (float)(rotationStart.X * Math.PI / 180f);
                    animation.RotationYStart = (float)(rotationStart.Y * Math.PI / 180f);
                    animation.RotationZStart = (float)(rotationStart.Z * Math.PI / 180f);
                }
                if (ImGui.DragFloat3($"Rotation End##{index}", ref rotationEnd))
                {
                    animation.RotationXEnd = (float)(rotationEnd.X * Math.PI / 180f);
                    animation.RotationYEnd = (float)(rotationEnd.Y * Math.PI / 180f);
                    animation.RotationZEnd = (float)(rotationEnd.Z * Math.PI / 180f);
                }
            }

            if  (ImGuiFlagBox(animation, $"Enable scaling##{index}", Sequence.ScalingFlag))
            {
                var scalePair = new Vector2(animation.ScaleStart, animation.ScaleEnd);
                if (ImGui.DragFloat2($"Scale##{index}", ref scalePair, 0.05f))
                {
                    animation.ScaleStart = scalePair.X;
                    animation.ScaleEnd = scalePair.Y;
                }

                var scaleXPair = new Vector2(animation.ScaleXStart, animation.ScaleXEnd);
                if (ImGui.DragFloat2($"Scale X##{index}", ref scaleXPair, 0.05f))
                {
                    animation.ScaleXStart = scaleXPair.X;
                    animation.ScaleXEnd = scaleXPair.Y;
                }

                var scaleYPair = new Vector2(animation.ScaleYStart, animation.ScaleYEnd);
                if (ImGui.DragFloat2($"Scale Y##{index}", ref scaleYPair, 0.05f))
                {
                    animation.ScaleYStart = scaleYPair.X;
                    animation.ScaleYEnd = scaleYPair.Y;
                }
            }

            var unk6xPair = new Vector4(
                animation.Unknown60, animation.Unknown64, animation.Unknown68, animation.Unknown6c);
            if (ImGui.DragFloat4($"Unknown6x##{index}", ref unk6xPair, 0.05f))
            {
                animation.Unknown60 = unk6xPair.X;
                animation.Unknown64 = unk6xPair.Y;
                animation.Unknown68 = unk6xPair.Z;
                animation.Unknown6c = unk6xPair.W;
            }

            if (ImGuiFlagBox(animation, "Enable bouncing", Sequence.BouncingFlag))
            {
                var bounceXPair = new Vector2(animation.BounceXStart, animation.BounceXEnd);
                if (ImGui.DragFloat2($"Bounce X##{index}", ref bounceXPair))
                {
                    animation.BounceXStart = bounceXPair.X;
                    animation.BounceXEnd = bounceXPair.Y;
                }

                var bounceYPair = new Vector2(animation.BounceYStart, animation.BounceYEnd);
                if (ImGui.DragFloat2($"Bounce Y##{index}", ref bounceYPair))
                {
                    animation.BounceYStart = bounceYPair.X;
                    animation.BounceYEnd = bounceYPair.Y;
                }

                var bounceSpeed = new int[] { animation.BounceXSpeed, animation.BounceYSpeed };
                if (ImGui.DragInt2($"Bounce speed##{index}", ref bounceSpeed[0]))
                {
                    animation.BounceXSpeed = (short)bounceSpeed[0];
                    animation.BounceYSpeed = (short)bounceSpeed[1];
                }
            }

            int blendMode = animation.ColorBlend;
            if (ImGui.Combo($"Blend mode##{index}", ref blendMode, new string[]
                { "Normal", "Additive", "Subtractive" }, 3))
                animation.ColorBlend = blendMode;

            if (ImGuiFlagBox(animation, $"Enable color mask##{index}", Sequence.ColorMaskingFlag))
            {
                var colorStart = Utilities.ConvertColor(animation.ColorStart);
                if (ImGui.ColorPicker4($"Mask start##{index}", ref colorStart))
                    animation.ColorStart = Utilities.ConvertColor(colorStart);

                if (ImGuiFlagBox(animation, $"Enable color mask animation##{index}", Sequence.ColorInterpolationFlag))
                {
                    var colorEnd = Utilities.ConvertColor(animation.ColorEnd);
                    if (ImGui.ColorPicker4($"Mask end##{index}", ref colorEnd))
                        animation.ColorEnd = Utilities.ConvertColor(colorEnd);
                }
            }
        }

        private unsafe void AnimationGroupSelector()
        {
            if (ImGui.BeginCombo("", $"Animation Group {SelectedAnimGroup}",
                ImGuiComboFlags.PopupAlignLeft))
            {
                DrawAnimationGroupList();
                ImGui.EndCombo();
            }
            ImGui.SameLine();
            if (ImGui.Button("-", new Vector2(30, 0)) && SelectedAnimGroup > 0)
                SelectedAnimGroup--;
            ImGui.SameLine();
            if (ImGui.Button("+", new Vector2(30, 0)) &&
                SelectedAnimGroup < _sequence.AnimationGroups.Count - 1)
                SelectedAnimGroup++;
        }

        private unsafe void Timeline()
        {
            if (SelectedAnimationGroup == null)
                return;

            var frameIndex = _renderer.GetActualFrame(SelectedAnimationGroup, _animationFrameCurrent);
            var frameIndexRef = _renderer.GetActualFrame(SelectedAnimationGroup, _animationFrameCurrent);

            ImGui.SliderInt("Frame", ref _animationFrameCurrent, 0, _animationFrameCount,
                $"%i/{_animationFrameCount}");
            var isSequenceExpanded = true;
            var isChanged = ImSequencer.Sequencer(_sequencer, ref frameIndexRef, ref isSequenceExpanded, ref _sequencerSelectedAnimation, ref _sequencerFirstFrame,
                ImSequencer.SEQUENCER_OPTIONS.SEQUENCER_EDIT_STARTEND |
                ImSequencer.SEQUENCER_OPTIONS.SEQUENCER_ADD |
                ImSequencer.SEQUENCER_OPTIONS.SEQUENCER_DEL |
                //ImSequencer.SEQUENCER_OPTIONS.SEQUENCER_COPYPASTE |
                ImSequencer.SEQUENCER_OPTIONS.SEQUENCER_CHANGE_FRAME);

            if (frameIndex != frameIndexRef)
                _animationFrameCurrent = frameIndexRef;
        }

        private bool ImGuiFlagBox(Sequence.Animation animation, string label, int flag, bool hideLabelWhenEnabled = false)
        {
            bool isSet = (animation.Flags & flag) == 0;
            if (isSet && hideLabelWhenEnabled)
                label = string.Empty;

            if (ImGui.Checkbox(label, ref isSet))
                animation.Flags = (animation.Flags & ~flag) | (isSet ? 0 : flag);

            return isSet;
        }

        private SpriteModel AsSpriteModel(Sequence.Sprite sprite) =>
            new SpriteModel(sprite, _drawing, _atlasTexture, this, _settings);
        private SpriteGroupModel AsSpriteGroupModel(int index) =>
            new SpriteGroupModel(_sequence, index, _drawing, _atlasTexture, this, _settings);

        private static Vector2 GetUv(ISpriteTexture texture, int x, int y) =>
            new Vector2((float)x / texture.Width, (float)y / texture.Height);

        public IntPtr BindTexture(Texture2D texture) =>
            _bootStrap.BindTexture(texture);

        public void UnbindTexture(IntPtr id) =>
            _bootStrap.UnbindTexture(id);

        public void RebindTexture(IntPtr id, Texture2D texture) =>
            _bootStrap.RebindTexture(id, texture);
    }
}
