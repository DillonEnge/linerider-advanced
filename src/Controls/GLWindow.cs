﻿#define debuggrid
//
//  GLWindow.cs
//
//  Author:
//       Noah Ablaseau <nablaseau@hotmail.com>
//
//  Copyright (c) 2017 
//
//  This program is free software: you can redistribute it and/or modify
//  it under the terms of the GNU General Public License as published by
//  the Free Software Foundation, either version 3 of the License, or
//  (at your option) any later version.
//
//  This program is distributed in the hope that it will be useful,
//  but WITHOUT ANY WARRANTY; without even the implied warranty of
//  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//  GNU General Public License for more details.
//
//  You should have received a copy of the GNU General Public License
//  along with this program.  If not, see <http://www.gnu.org/licenses/>.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Threading;
using System.Windows.Forms;
using Gwen;
using Gwen.Controls;
using linerider.Audio;
using linerider.Drawing;
using linerider.TrackFiles;
using linerider.Tools;
using linerider.UI;
using OpenTK;
using OpenTK.Graphics;
using OpenTK.Graphics.OpenGL;
using OpenTK.Input;
using Key = OpenTK.Input.Key;
using Label = Gwen.Controls.Label;
using Menu = Gwen.Controls.Menu;
using PixelFormat = System.Drawing.Imaging.PixelFormat;

namespace linerider
{
    // ReSharper disable once InconsistentNaming
    public class GLWindow : OpenTK.GameWindow
    {
        private enum Tools
        {
            None = 0,
            HandTool,
            LineTool,
            PencilTool,
            EraserTool,
            LineAdjustTool,
            GwellTool
        }

        public static readonly int[] MotionArray =
        {
            1, 2, 5, 10, 20, 30, 40, 80, 160, 320, 640
        };

        public static readonly Color4 ColorOffwhite = new Color4(244, 245, 249, 255);
        public static readonly Color4 ColorWhite = new Color4(255, 255, 255, 255);
        public static readonly Color4 ColorNightMode = new Color4(20, 20, 25, 255);
        private static GLWindow _instance;
        public readonly GameScheduler Scheduler = new GameScheduler();
        private readonly Tool _penciltool;
        private readonly Tool _linetool;
        private readonly Tool _erasertool;
        private readonly Tool _handtool;
        private readonly LineAdjustTool _lineadjusttool;
        private readonly Stopwatch _autosavewatch = Stopwatch.StartNew();
        public MsaaFbo MSAABuffer;
        public Dictionary<string, MouseCursor> Cursors = new Dictionary<string, MouseCursor>();
        public Song CurrentSong = new Song("", 0);
        public bool EnableSong = false;
        public GameCanvas Canvas;
        public bool Loading = false;
        public float SettingDefaultPlayback = 1f;
        public bool SettingDisableSnap = false;
        public bool SettingForceXySnap = false;
        public bool SettingMomentumVectors = false;
        public bool SettingPreviewMode;
        public int SettingSlowmoSpeed = 1;
        public bool SettingRecordingMode;
        public bool SettingRenderGravityWells;
        public bool SettingColorPlayback;
        public bool SettingDrawContactPoints;
        public bool SettingOnionSkinning;
        //recording:
        public bool SettingRecordingShowTools = false;
        public bool SettingShowFps = true;
        public bool SettingShowPpf = true;
        public bool SettingShowTimer = true;
        public Tool SelectedTool;
        public bool AllowTrackRender;

        public bool ReversePlayback = false;
        public bool TemporaryPlayback = false;

        private bool _handToolOverride;
        private float _zoomPerTick;
        private Gwen.Input.OpenTK _input;
        private bool _dragRider;
        private bool __controlsInitialized;

        public bool EnableSnap
        {
            get { return !SettingDisableSnap && !OpenTK.Input.Keyboard.GetState().IsKeyDown(Key.S); }
        }

        public Size RenderSize
        {
            get
            {
                if (TrackRecorder.Recording)
                {
                    return TrackRecorder.Recording1080p ? new Size(1920, 1080) : new Size(1280, 720);
                }
                return ClientSize;
            }
            set
            {
                ClientSize = value;
            }
        }
        public Vector2d ScreenPosition
            => Track.Camera.GetViewport().Vector;

        public Vector2d ScreenTranslation => -ScreenPosition;
        public TrackService Track { get; }
        public GLWindow()
            : base(1280, 720, new GraphicsMode(new ColorFormat(32), 0, 0, 0, ColorFormat.Empty),
                   "Line Rider: Advanced", GameWindowFlags.Default, DisplayDevice.Default)
        {
            if (_instance != null)
                throw new InvalidOperationException();

            SafeFrameBuffer.Initialize();
            _instance = this;
            StaticRenderer.InitializeCircles();
            _penciltool = new PencilTool();
            _linetool = new LineTool();
            _erasertool = new EraserTool();
            _handtool = new HandTool();
            _lineadjusttool = new LineAdjustTool();
            SelectedTool = _penciltool;
            Track = new TrackService();
            VSync = VSyncMode.On;
            Context.ErrorChecking = true;
            WindowBorder = WindowBorder.Resizable;
            RenderFrame += (o, e) => { Render(); };
            UpdateFrame += (o, e) => { GameUpdate(); };
            GameService.Initialize(this);
        }

        public override void Dispose()
        {
            if (Canvas != null)
            {
                Canvas.Dispose();
                Canvas.Skin.DefaultFont.Dispose();
                Canvas.Skin.Dispose();
                Canvas.Renderer.Dispose();
                Canvas = null;
            }
            base.Dispose();
        }

        public bool ShouldXySnap()
        {
            return SettingForceXySnap || OpenTK.Input.Keyboard.GetState()[Key.X];
        }

        public void Zoom(float f, bool changezoomslider = true)
        {
            float maxzoom = Settings.SuperZoom ? 200 : 24;
            if ((Track.Zoom >= maxzoom && f > 0) || (Track.Zoom <= 0.1f && f < 0) || Math.Abs(f) < 0.001)
                return;
            Track.Zoom += f;
            if (Track.Zoom < 0.1f)
                Track.Zoom = 0.1f;
            if (Track.Zoom > maxzoom)
                Track.Zoom = maxzoom;
            Invalidate();
            VerticalSlider vslider = (VerticalSlider)Canvas.FindChildByName("vslider", true);

            if (changezoomslider)
            {
                if (vslider != null)
                    vslider.Value = Track.Zoom;
            }
            vslider.SetToolTipText(Math.Round(Track.Zoom, 2) + "x");
        }
        public void Render(float blend = 1)
        {
            bool shouldrender = Canvas.NeedsRedraw ||
            (Track.Animating && (Track.SimulationNeedsDraw || Settings.SmoothPlayback)) ||
            Loading ||
            Track.RequiresUpdate ||
            SelectedTool.NeedsRender;

            if (shouldrender)
            {
                Track.SimulationNeedsDraw = false;

                BeginOrtho();
                var slider = (HorizontalIntSlider)Canvas.FindChildByName("timeslider");
                if (blend == 1 && Settings.SmoothPlayback && Track.Playing && !TemporaryPlayback && !slider.Held)
                {
                    blend = Math.Min(1, Scheduler.ElapsedPercent);
                }
                Track.Camera.BeginFrame(blend);
                GL.ClearColor(Settings.NightMode
                    ? ColorNightMode
                    : (Settings.WhiteBG ? ColorWhite : ColorOffwhite));
                MSAABuffer.Use(RenderSize.Width, RenderSize.Height);
                GL.BlendFunc(BlendingFactorSrc.SrcAlpha, BlendingFactorDest.OneMinusSrcAlpha);
                GL.Clear(ClearBufferMask.ColorBufferBit);
                GL.Enable(EnableCap.Blend);
#if debuggrid
                GameRenderer.DbgDrawGrid();
#endif

                Track.Render(blend);
                SelectedTool.Render();
                Canvas.RenderCanvas();
                MSAABuffer.End();

                if (Settings.NightMode)
                {
                    StaticRenderer.RenderRect(new FloatRect(0, 0, RenderSize.Width, RenderSize.Height), Color.FromArgb(40, 0, 0, 0));
                }
                if (!TrackRecorder.Recording)
                    SwapBuffers();
                //there are machines and cases where a refresh may not hit the screen without calling glfinish...
                GL.Finish();
                var seconds = Track.Fpswatch.Elapsed.TotalSeconds;
                Track.FpsCounter.AddFrame(seconds);
                Track.Fpswatch.Restart();
            }
            if (!Track.Playing && !Canvas.NeedsRedraw && !Track.RequiresUpdate)//if nothing is waiting on us we can let the os breathe
                Thread.Sleep(1);
        }

        public void GameUpdate()
        {
            var updates = Scheduler.UnqueueUpdates();
            if (updates > 0)
            {
                Zoom(Math.Min(Track.Zoom, 12) * (_zoomPerTick));
            }
            if (_autosavewatch.Elapsed.TotalMinutes >= 5)
            {
                _autosavewatch.Restart();
                new Thread(() => { Track.BackupTrack(false); }).Start();
            }
            if (Canvas.GetOpenWindows().Count != 0)
            {
                Invalidate();
            }
            if (Track.Playing || TemporaryPlayback)
            {
                if (TemporaryPlayback && ReversePlayback)
                {
                    Track.ActiveTriggers.Clear();//we don't want wonky unpredictable behavior
                    for (int i = 0; i < updates; i++)
                    {
                        Track.PreviousFrame();
                        Track.UpdateCamera();
                    }
                }
                else
                {
                    Track.Update(updates);
                }
                if (Track.Offset % Math.Max(1, Scheduler.UpdatesPerSecond / 2) == 0)
                {
                    var sp = AudioService.SongPosition;
                    if (Math.Abs(((Track.CurrentFrame / 40f) + CurrentSong.Offset) - sp) > 0.1)
                    {
                        UpdateSongPosition(Track.CurrentFrame / 40f);
                    }
                }
            }
            if (Track.Animating && (Track.Paused || Settings.SmoothPlayback))
                AllowTrackRender = true;
            if (Program.NewVersion != null)
            {
                Canvas.ShowOutOfDate();
            }
        }

        public void Invalidate()
        {
            if (Canvas != null)
                Canvas.NeedsRedraw = true;
        }

        public void InvalidateTrack()
        {
            Track.RequiresUpdate = true;
        }

        public void UpdateSongPosition(float seconds, bool reverse = false)
        {
            if (EnableSong && (!Track.Paused || TemporaryPlayback) && Track.Animating &&
                !((HorizontalIntSlider)Canvas.FindChildByName("timeslider")).Held)
            {
                if (TemporaryPlayback && ReversePlayback)
                    AudioService.Resume(CurrentSong.Offset + seconds, -(Scheduler.UpdatesPerSecond / 40f));
                else
                    AudioService.Resume(CurrentSong.Offset + seconds, (Scheduler.UpdatesPerSecond / 40f));
            }
            else
            {
                AudioService.Pause();
            }
        }

        public void UpdateCursor()
        {
            if ((Track.Animating && !Track.Paused) || _dragRider)
                Cursor = Cursors["default"];
            else if (_handToolOverride)
                Cursor = _handtool.Cursor;
            else if (SelectedTool != null)
                Cursor = SelectedTool.Cursor;
        }

        protected override void OnLoad(EventArgs e)
        {
            MSAABuffer = new MsaaFbo();
            var renderer = new Gwen.Renderer.OpenTK();

            var skinpng = new Texture(renderer);
            Gwen.Renderer.OpenTK.LoadTextureInternal(skinpng, GameResources.DefaultSkin);
            var fontpng = new Texture(renderer);
            Gwen.Renderer.OpenTK.LoadTextureInternal(fontpng, GameResources.gamefont_15_png);
            var gamefont_15 = new Gwen.Renderer.BitmapFont(renderer, GameResources.gamefont_15_fnt, fontpng);
            var skin = new Gwen.Skin.TexturedBase(renderer, skinpng, GameResources.DefaultColors) { DefaultFont = gamefont_15 };
            Canvas = new GameCanvas(skin, this, renderer);
            _input = new Gwen.Input.OpenTK(this);
            _input.Initialize(Canvas);
            Canvas.ShouldDrawBackground = false;
            InitControls();
            Models.LoadModels();

            AddCursor("pencil", GameResources.cursor_pencil, 3, 28);
            AddCursor("line", GameResources.cursor_line, 15, 15);
            AddCursor("eraser", GameResources.cursor_eraser, 5, 5);
            AddCursor("hand", GameResources.cursor_move, 16, 16);
            AddCursor("closed_hand", GameResources.cursor_dragging, 16, 16);
            AddCursor("adjustline", GameResources.cursor_select, 12, 12);
            AddCursor("size_nesw", GameResources.cursor_size_nesw, 16, 16);
            AddCursor("size_nwse", GameResources.cursor_size_nwse, 16, 16);
            AddCursor("size_hor", GameResources.cursor_size_horz, 16, 16);
            AddCursor("size_ver", GameResources.cursor_size_vert, 16, 16);
            AddCursor("size_all", GameResources.cursor_size_all, 16, 16);
            AddCursor("default", GameResources.cursor_default, 7, 4);
            AddCursor("zoom", GameResources.cursor_zoom_in, 11, 10);
            Gwen.Platform.Neutral.CursorSetter = new Utils.CursorImpl(this);
            Program.UpdateCheck();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            Canvas.SetSize(RenderSize.Width, RenderSize.Height);
            Canvas.FindChildByName("buttons").Position(Pos.CenterH);
        }

        protected override void OnMouseDown(MouseButtonEventArgs e)
        {
            base.OnMouseDown(e);
            if (linerider.TrackFiles.TrackRecorder.Recording)
                return;
            var r = _input.ProcessMouseMessage(e);

            if (!r && (!Track.Animating || Track.Paused))
            {
                if (!Track.Paused && OpenTK.Input.Keyboard.GetState()[Key.D])
                {
                    var pos = new Vector2d(e.X, e.Y) / Track.Zoom;
                    var gamepos = (ScreenPosition + pos);
                    _dragRider = Track.RiderRect.Contains((float)gamepos.X, (float)gamepos.Y);
                }
                if (!_dragRider)
                {
                    if (e.Button == MouseButton.Left)
                    {
                        if (_handToolOverride)
                            _handtool.OnMouseDown(new Vector2d(e.X, e.Y));
                        else
                            SelectedTool.OnMouseDown(new Vector2d(e.X, e.Y));
                    }
                    else if (e.Button == MouseButton.Right)
                    {
                        if (_handToolOverride)
                            _handtool.OnMouseRightDown(new Vector2d(e.X, e.Y));
                        else
                            SelectedTool.OnMouseRightDown(new Vector2d(e.X, e.Y));
                    }
                    else if (e.Button == MouseButton.Middle)
                    {
                        _handToolOverride = true;
                        _handtool.OnMouseDown(new Vector2d(e.X, e.Y));
                    }
                }
                if (e.Button != MouseButton.Right)
                {
                    UpdateCursor();
                }
            }
            else
            {
                Cursor = Cursors["default"];
            }
            Invalidate();
        }

        protected override void OnMouseUp(MouseButtonEventArgs e)
        {
            base.OnMouseUp(e);
            if (linerider.TrackFiles.TrackRecorder.Recording)
                return;
            _dragRider = false;
            var r = _input.ProcessMouseMessage(e);
            if (Canvas.GetOpenWindows().Count != 0)
                return;
            if (e.Button == MouseButton.Left)
            {
                if (_handToolOverride)
                    _handtool.OnMouseUp(new Vector2d(e.X, e.Y));
                else
                    SelectedTool.OnMouseUp(new Vector2d(e.X, e.Y));
            }
            else if (e.Button == MouseButton.Right)
            {
                if (_handToolOverride)
                    _handtool.OnMouseRightUp(new Vector2d(e.X, e.Y));
                else
                    SelectedTool.OnMouseRightUp(new Vector2d(e.X, e.Y));
            }
            else if (e.Button == MouseButton.Middle)
            {
                _handToolOverride = false;
                _handtool.OnMouseUp(new Vector2d(e.X, e.Y));
            }
            if (r)
                Cursor = Cursors["default"];
            else
                UpdateCursor();
        }

        protected override void OnMouseMove(MouseMoveEventArgs e)
        {
            base.OnMouseMove(e);
            if (linerider.TrackFiles.TrackRecorder.Recording)
                return;
            var r = _input.ProcessMouseMessage(e);
            if (Canvas.GetOpenWindows().Count != 0)
                return;
            if (_dragRider)
            {
                var pos = new Vector2d(e.X, e.Y);
                var gamepos = ScreenPosition + (pos / Track.Zoom);
                using (var trk = Track.CreateTrackWriter())
                {
                    using (Track.EnterPlayback())
                    {
                        trk.Track.StartOffset = gamepos;
                        Track.Reset();
                        Track.TrackUpdated();
                    }
                }
                Invalidate();
            }
            else if (_handToolOverride)
                _handtool.OnMouseMoved(new Vector2d(e.X, e.Y));
            else
                SelectedTool.OnMouseMoved(new Vector2d(e.X, e.Y));

            if (r)
            {
                Cursor = Cursors["default"];
                Invalidate();
            }
            else
                UpdateCursor();
        }

        protected override void OnMouseWheel(MouseWheelEventArgs e)
        {
            base.OnMouseWheel(e);
            if (linerider.TrackFiles.TrackRecorder.Recording)
                return;
            if (_input.ProcessMouseMessage(e))
                return;
            if (Canvas.GetOpenWindows().Count != 0)
                return;
            var delta = (float.IsNaN(e.DeltaPrecise) ? e.Delta : e.DeltaPrecise);
            Zoom((Track.Zoom / 50) * delta);
        }

        protected override void OnKeyDown(KeyboardKeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (linerider.TrackFiles.TrackRecorder.Recording)
                return;
            var openwindows = Canvas.GetOpenWindows();
            var mod = e.Modifiers;
            if (openwindows != null && openwindows.Count >= 1)
            {
                if (e.Key == Key.Escape)
                {
                    foreach (var v in openwindows)
                    {
                        ((WindowControl)v).Close();
                        Invalidate();
                    }
                    return;
                }
            }
            if (_input.ProcessKeyDown(e) || Canvas.IsModalOpen)
            {
                return;
            }
            if (_dragRider || OpenTK.Input.Mouse.GetState().IsButtonDown(MouseButton.Left))
            {
                if (!_handToolOverride && (!Track.Animating || (Track.Animating && Track.Paused)) &&
                    SelectedTool != null)
                {
                    if (SelectedTool.OnKeyDown(e.Key))
                        return;
                }
                else
                {
                    return;
                }
            }
            var input = e.Keyboard;

            #region CTRL+

            if (input.IsKeyDown(Key.ControlLeft) || input.IsKeyDown(Key.ControlRight))
            {
                if (e.Key == Key.Z)
                {
                    SelectedTool?.Stop(); //BUGFIX SLAGwell removal.
                    var u = Track.UndoManager.Undo();
                    Invalidate();
                    if (u)
                        Track.TrackUpdated();
                    return;
                }
                if (e.Key == Key.Y)
                {
                    SelectedTool?.Stop();
                    var r = Track.UndoManager.Redo();
                    Invalidate();
                    if (r)
                        Track.TrackUpdated();
                    return;
                }
                if (e.Key == Key.S)
                {
                    Canvas.ShowSaveWindow();
                    return;
                }
                if (e.Key == Key.O)
                {
                    SettingOnionSkinning = !SettingOnionSkinning;
                    Invalidate();
                    return;
                }
                if (e.Key == Key.P)
                {
                    Canvas.ShowPreferences();
                    return;
                }
            }
            #endregion
            #region ALT+

            else if (input.IsKeyDown(Key.AltLeft) || input.IsKeyDown(Key.AltRight))
            {
                if (input.IsKeyDown(Key.Y))
                {
                    Track.Start(true);
                    return;
                }
                if (SelectedTool == _lineadjusttool)
                {
                    _lineadjusttool.CanLifelock = true;
                    Invalidate();
                }
                if (input.IsKeyDown(Key.Enter))
                {
                    if (WindowBorder == WindowBorder.Resizable)
                    {
                        WindowBorder = WindowBorder.Hidden;
                        X = 0;
                        Y = 0;
                        var area = Screen.PrimaryScreen.Bounds;
                        RenderSize = area.Size;
                    }
                    else
                    {
                        WindowBorder = WindowBorder.Resizable;
                    }
                    return;
                }


                if (Track.Animating && Track.Paused && Track.Offset != 0)
                {
                    if (input.IsKeyDown(Key.Right))
                    {
                        if (Track.IterationsOffset < 6)
                        {
                            Track.IterationsOffset++;
                            Canvas.UpdateIterationUI();
                        }
                        else
                        {
                            Track.NextFrame();
                            Invalidate();
                            Canvas.UpdateIterationUI();
                            Track.Camera.SetFrame(Track.RenderRider.CalculateCenter(), false);
                        }
                        return;
                    }
                    if (input.IsKeyDown(Key.Left))
                    {
                        if (Track.IterationsOffset > 0)
                        {
                            Track.IterationsOffset--;
                            Canvas.UpdateIterationUI();
                        }
                        else
                        {
                            Track.PreviousFrame();
                            Invalidate();
                            Canvas.UpdateIterationUI();
                            Track.Camera.SetFrame(Track.RenderRider.CalculateCenter(), false);
                        }
                        return;
                    }
                }
            }

            #endregion

            if (input.IsKeyDown(Key.ShiftLeft) || input.IsKeyDown(Key.ShiftRight))
            {
                if (input.IsKeyDown(Key.Y))
                {
                    Track.Start(false, true, false);
                    Scheduler.UpdatesPerSecond = SettingSlowmoSpeed;
                    UpdateSongPosition(Track.CurrentFrame / 40f);
                    return;
                }
                if (input.IsKeyDown(Key.I))
                {
                    Track.Start(true, true, true, true);
                    return;
                }

                if (input.IsKeyDown(Key.Right))
                {
                    if (!TemporaryPlayback && !Track.Playing)
                    {
                        ReversePlayback = false;
                        TemporaryPlayback = true;
                        if (!Track.Animating)
                        {
                            Track.Start();
                        }
                        Scheduler.Reset();
                        UpdateSongPosition(Track.CurrentFrame / 40f);
                    }
                    return;
                }
                if (input.IsKeyDown(Key.Left))
                {
                    if (!TemporaryPlayback)
                    {
                        if (Track.Animating)
                        {
                            ReversePlayback = true;
                            TemporaryPlayback = true;
                        }
                        Scheduler.Reset();
                        UpdateSongPosition(Track.CurrentFrame / 40f);
                    }
                    return;
                }
            }
            if (!e.Control && !e.Shift && !e.Alt)
            {
                if (e.Key == Key.Q)
                {
                    SetTool(Tools.PencilTool);
                }
                else if (e.Key == Key.W)
                {
                    SetTool(Tools.LineTool);
                }
                else if (e.Key == Key.E)
                {
                    SetTool(Tools.EraserTool);
                }
                else if (e.Key == Key.R)
                {
                    SetTool(Tools.LineAdjustTool);
                }
                else if (e.Key == Key.T)
                {
                    if (SelectedTool != _handtool)
                    {
                        SetTool(Tools.HandTool);
                    }
                }
                else if (e.Key == Key.Y)
                {
                    Track.Start();
                }
                else if (e.Key == Key.U)
                {
                    Track.Stop();
                }
                else if (e.Key == Key.I)
                {
                    Track.Flag();
                }
                else if (e.Key == Key.O)
                {
                    Canvas.ShowLoadWindow();
                }
                else if (e.Key == Key.M)
                {
                    if (Track.Animating)
                    {
                        if (Math.Abs(Scheduler.UpdatesPerSecond - 40) < 0.01)
                        {
                            Scheduler.UpdatesPerSecond = SettingSlowmoSpeed;
                            if (EnableSong)
                            {
                                UpdateSongPosition(Track.CurrentFrame / 40f);
                            }
                        }
                        else
                        {
                            Scheduler.UpdatesPerSecond = 40;
                            if (EnableSong)
                            {
                                UpdateSongPosition(Track.CurrentFrame / 40f);
                            }
                        }
                    }
                }
                else if (e.Key == Key.Z)
                {
                    if (Track.Playing)
                        _zoomPerTick = 0.08f;
                }
                else if (e.Key == Key.X)
                {
                    if (Track.Playing)
                        _zoomPerTick = -0.08f;
                }
                if (e.Key == Key.Space)
                {
                    if (!Track.Animating)
                    {
                        _handToolOverride = true;
                    }
                    else
                    {
                        if (!SettingRecordingMode)
                        {
                            Track.TogglePause();
                        }
                    }
                }
                else if (e.Key == Key.F1)
                {
                    if (!Track.Animating)
                    {
                        Track.Camera.SetFrame(Track.RenderRider.Body[4].Location, false);
                        Invalidate();
                    }
                }
                else if (e.Key == Key.F2)
                {
                    if (!Track.Animating)
                    {
                        var flag = Track.GetFlag();
                        if (flag != null)
                        {
                            Track.Camera.SetFrame(flag.State.Body[4].Location, false);
                            Invalidate();
                        }
                    }
                }
                else if (e.Key == Key.Number1)
                {
                    Canvas.ColorControls.Selected = LineType.Blue;
                    Invalidate();
                }
                else if (e.Key == Key.Number2)
                {
                    Canvas.ColorControls.Selected = LineType.Red;
                    Invalidate();
                }
                else if (e.Key == Key.Number3)
                {
                    Canvas.ColorControls.Selected = LineType.Scenery;
                    Invalidate();
                }
                else if (e.Key == Key.Minus || e.Key == Key.KeypadMinus)
                {
                    PlaybackDown();
                }
                else if (e.Key == Key.Plus || e.Key == Key.KeypadPlus)
                {
                    PlaybackUp();
                }
                else if (e.Key == Key.BackSpace)
                {
                    if (!Track.Animating || Track.Paused)
                    {
                        using (var trk = Track.CreateTrackWriter())
                        {
                            SelectedTool?.Stop(); //BUGFIX SLAGwell removal.
                            var l = trk.GetLastLine();
                            if (l != null)
                            {
                                Track.UndoManager.BeginAction();
                                trk.RemoveLine(l);
                                Track.UndoManager.EndAction();
                            }
                            Track.TrackUpdated();
                            Invalidate();
                        }
                    }
                }
                else if (e.Key == Key.Right)
                {
                    if (!TemporaryPlayback)
                    {
                        if (!Track.Animating)
                        {
                            Track.Start();
                        }
                        if (!Track.Paused)
                            Track.TogglePause();
                        Track.NextFrame();
                        Invalidate();
                        Track.UpdateCamera();
                    }
                }
                else if (e.Key == Key.Left)
                {
                    if (!TemporaryPlayback)
                    {
                        if (!Track.Animating)
                        {
                            Track.Start();
                        }
                        if (!Track.Paused)
                            Track.TogglePause();
                        Track.PreviousFrame();
                        Invalidate();
                        Track.UpdateCamera();
                    }
                }
                else if (e.Key == Key.Home)
                {
                    using (var trk = Track.CreateTrackReader())
                    {
                        var l = trk.GetFirstLine();
                        if (l != null)
                        {
                            Track.Camera.SetFrame(l.Position, false);
                            Invalidate();
                        }
                    }
                }
                else if (e.Key == Key.End)
                {
                    using (var trk = Track.CreateTrackReader())
                    {
                        var l = trk.GetLastLine();
                        if (l != null)
                        {
                            Track.Camera.SetFrame(l.Position, false);
                            Invalidate();
                        }
                    }
                }
                else if (e.Key == Key.Tab)
                {
                    if (!Track.Playing)
                    {
                        Canvas.ColorControls.OnTabButtonPressed();
                    }
                }
                else if (e.Key == Key.Escape)
                {
                    Canvas.ShowPreferences();
                }
                else if (e.Key == Key.F12)
                {
                }
            }
        }

        protected override void OnKeyUp(KeyboardKeyEventArgs e)
        {
            base.OnKeyUp(e);
            if (linerider.TrackFiles.TrackRecorder.Recording)
                return;
            if (_input.ProcessKeyUp(e) || Canvas.GetOpenWindows()?.Count > 1)
                return;
            if (e.Key == Key.Space)
            {
                _handToolOverride = false;
                _handtool.Stop();
            }
            else if (e.Key == Key.AltLeft || e.Key == Key.AltRight)
            {
                _lineadjusttool.CanLifelock = false;
                if (SelectedTool == _lineadjusttool)
                {
                    Invalidate();
                }
            }
            else if (e.Key == Key.Right || e.Key == Key.Left)
            {
                if (TemporaryPlayback)
                {
                    TemporaryPlayback = false;
                    Scheduler.Reset();
                    AudioService.Pause();
                }
            }
            if (e.Key == Key.Z)
            {
                if (_zoomPerTick > 0)
                    _zoomPerTick = 0;
            }
            else if (e.Key == Key.X)
            {
                if (_zoomPerTick < 0)
                    _zoomPerTick = 0;
            }
        }

        internal void InitControls()
        {
            if (__controlsInitialized)
                throw new Exception("Controls reinitialized");
            var ctrl = new ControlBase(Canvas);
            ctrl.Name = "buttons";
            var pos = 0;
            Func<Bitmap, Bitmap, string, string, ImageButton> createbutton =
                delegate (Bitmap bmp, Bitmap bmp2, string tooltip, string name)
                {
                    var ret = new ImageButton(ctrl) { Name = name };
                    if (tooltip != null)
                        ret.SetToolTipText(tooltip);
                    ret.SetImage(bmp, bmp2);
                    ret.SetSize(32, 32);
                    ret.X = pos;
                    pos += 32;
                    return ret;
                };
            //Warning:
            //the name parameter needs to stay consistent for these buttons
            //other parts of code reference it.
            var btn = createbutton(GameResources.pencil_icon, GameResources.pencil_icon_white, "Pencil Tool (Q)", "penciltool");
            btn.Clicked += (o, e) => { SetTool(Tools.PencilTool); };
            btn = createbutton(GameResources.line_icon, GameResources.line_icon_white, "Line Tool (W)", "linetool");
            btn.Clicked += (o, e) => { SetTool(Tools.LineTool); };
            btn = createbutton(GameResources.eraser_icon, GameResources.eraser_icon_white, "Eraser Tool (E)", "erasertool");
            btn.Clicked += (o, e) => { SetTool(Tools.EraserTool); };
            btn = createbutton(GameResources.line_adjust_icon, GameResources.line_adjust_icon_white, "Line Adjustment Tool (R)",
                "lineadjusttool");
            btn.Clicked += (o, e) => { SetTool(Tools.LineAdjustTool); };
            //  btn = createbutton(Content.gwell_tool, Content.gwell_tool, "Gravity Well Tool (T)",
            //       "gwelltool");
            //   btn.Clicked += (o, e) => { SetTool(Tools.GwellTool); };
            btn = createbutton(GameResources.move_icon, GameResources.move_icon_white, "Hand Tool (Space) (T)", "handtool");
            btn.Clicked += (o, e) =>
            {
                SetTool(Tools.HandTool);
                _handToolOverride = false;
            };
            btn = createbutton(GameResources.play_icon, GameResources.play_icon_white, "Start (Y)", "start");
            btn.Clicked += (o, e) =>
            {
                if (Track.Animating && Track.Paused)
                {
                    Track.TogglePause();
                }
                else
                {
                    Track.Start();
                }
            };
            pos -= 32; //occupy same space as the start button
            btn = createbutton(GameResources.pause, GameResources.pause_white, null, "pause");
            btn.IsHidden = true;
            btn.Clicked += (o, e) => { Track.TogglePause(); };
            btn = createbutton(GameResources.stop_icon, GameResources.stop_icon_white, "Stop (U)", "stop");
            btn.Clicked += (o, e) => { Track.Stop(); };
            btn = createbutton(GameResources.flag_icon, GameResources.flag_icon_white, "Flag (I)", "flag");
            btn.Clicked += (o, e) => { Track.Flag(); };
            btn.RightClicked += (o, e) => { Canvas.CalculateFlag(Track.GetFlag()); };
            Canvas.FlagTool = btn;
            btn = createbutton(GameResources.menu_icon, GameResources.menu_icon_white, "Menu", "menu");
            var _menuEdit = new Menu(Canvas);
            var item = _menuEdit.AddItem("Save");
            item.AutoSizeToContents = false;
            item.Clicked += (snd, evt) => { Canvas.ShowSaveWindow(); };
            item = _menuEdit.AddItem("Load");
            item.AutoSizeToContents = false;
            item.Clicked += (snd, evt) => { Canvas.ShowLoadWindow(); };
            item = _menuEdit.AddItem("Delete");
            item.AutoSizeToContents = false;
            item.Clicked += (snd, evt) => { Canvas.ShowDelete(); };
            item = _menuEdit.AddItem("Preferences");
            item.AutoSizeToContents = false;
            item.Clicked += (snd, msg) => { Canvas.ShowPreferences(); };
            item = _menuEdit.AddItem("Song");
            item.AutoSizeToContents = false;
            item.Clicked += (snd, msg) => { Canvas.ShowSongWindow(); };
            item = _menuEdit.AddItem("Export SOL");
            item.AutoSizeToContents = false;
            item.Clicked += (snd, msg) =>
            {
                Canvas.ExportAsSol();
            };
            item = _menuEdit.AddItem("Export Video");
            item.AutoSizeToContents = false;
            item.Clicked += (snd, msg) =>
            {
                if (SafeFrameBuffer.CanRecord)
                {
                    ExportVideoWindow.Create(this);
                }
                else
                {
                    PopupWindow.Error("This computer does not support recording.\nTry updating your graphics drivers.");
                }
            };
            btn.Clicked += (o, e) =>
            {
                var canvaspos = ctrl.LocalPosToCanvas(new Point(btn.X, btn.Y));
                _menuEdit.MoveTo(canvaspos.X, canvaspos.Y + 32);
                _menuEdit.Show();
            };
            _menuEdit.DeleteOnClose = false;
            _menuEdit.Close();
            var cc = new ColorControls(ctrl, new Vector2(0, 32 + 3));
            cc.Selected = LineType.Blue;
            Canvas.ColorControls = cc;
            ctrl.SizeToChildren();
            ctrl.ShouldCacheToTexture = true;
            ctrl.Position(Pos.CenterH);

            __controlsInitialized = true;
            Canvas.ButtonsToggleNightmode();
        }

        public void PlaybackUp()
        {
            if (Track.Animating)
            {
                var index = Array.IndexOf(MotionArray, Scheduler.UpdatesPerSecond);
                Scheduler.UpdatesPerSecond = MotionArray[Math.Min(MotionArray.Length - 1, index + 1)];
                if (EnableSong)
                {
                    UpdateSongPosition(Track.CurrentFrame / 40f);
                }
            }
        }

        public void PlaybackDown()
        {
            if (Track.Animating)
            {
                var index = Array.IndexOf(MotionArray, Scheduler.UpdatesPerSecond);
                Scheduler.UpdatesPerSecond = MotionArray[Math.Max(0, index - 1)];
                if (EnableSong)
                {
                    UpdateSongPosition(Track.CurrentFrame / 40f);
                }
            }
        }

        private void BeginOrtho()
        {
            if (RenderSize.Height > 0 && RenderSize.Width > 0)
            {
                GL.Viewport(new Rectangle(0, 0, RenderSize.Width, RenderSize.Height));
                GL.MatrixMode(MatrixMode.Projection);
                GL.LoadIdentity();
                GL.Ortho(0, RenderSize.Width, RenderSize.Height, 0, 0, 1);
                GL.MatrixMode(MatrixMode.Modelview);
                GL.LoadIdentity();
            }
        }

        private void SetTool(Tools tool)
        {
            if (SelectedTool != null)
            {
                SelectedTool.Stop();
                SelectedTool.OnChangingTool();
            }
            if (SelectedTool == _erasertool && tool != Tools.EraserTool)
            {
                Canvas.ColorControls.SetEraser(false);
            }
            if (tool == Tools.HandTool)
            {
                SelectedTool = _handtool;
                _handtool.Stop();
                _handToolOverride = false;
                Canvas.ColorControls.SetVisible(false);
            }
            else if (tool == Tools.LineTool)
            {
                SelectedTool = _linetool;
                Canvas.ColorControls.SetVisible(true);
                if (Canvas.ColorControls.Selected == LineType.All)
                {
                    Canvas.ColorControls.Selected = LineType.Blue;
                }
            }
            else if (tool == Tools.PencilTool)
            {
                SelectedTool = _penciltool;
                Canvas.ColorControls.SetVisible(true);
                if (Canvas.ColorControls.Selected == LineType.All)
                {
                    Canvas.ColorControls.Selected = LineType.Blue;
                }
            }
            else if (tool == Tools.EraserTool)
            {
                Canvas.ColorControls.SetVisible(true);
                Canvas.ColorControls.SetEraser(true);
                if (SelectedTool == _erasertool)
                    Canvas.ColorControls.Selected = LineType.All;
                SelectedTool = _erasertool;
            }
            else if (tool == Tools.LineAdjustTool)
            {
                SelectedTool = _lineadjusttool;
                Canvas.ColorControls.SetVisible(false);
            }
            UpdateCursor();
            Invalidate();
        }

        private void AddCursor(string name, Bitmap image, int hotx, int hoty)
        {
            var data = image.LockBits(
                new Rectangle(0, 0, image.Width, image.Height),
                ImageLockMode.ReadOnly,
                PixelFormat.Format32bppPArgb);
            Cursors[name] = new MouseCursor(hotx, hoty, image.Width, image.Height, data.Scan0);
        }
    }
}