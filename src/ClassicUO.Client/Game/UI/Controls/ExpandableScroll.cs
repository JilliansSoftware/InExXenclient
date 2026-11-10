// SPDX-License-Identifier: BSD-2-Clause

using ClassicUO.Game.Managers;
using ClassicUO.Input;
using ClassicUO.Assets;
using ClassicUO.Renderer;
using Microsoft.Xna.Framework;

namespace ClassicUO.Game.UI.Controls
{
    internal class ExpandableScroll : Control
    {
        private const int c_ExpandableScrollHeight_Min = 274;
        private const int c_ExpandableScrollHeight_Max = 800;
        private const int c_GumplingExpanderY_Offset = 2; // this is the gap between the pixels of the btm Control texture and the height of the btm Control texture.
        private const int c_GumplingExpander_ButtonID = 0x7FBEEF;
        private readonly GumpPic _gumpBottom;
        private Button _gumpExpander;
        private GumpPic _gumplingTitle;
        private int _gumplingTitleGumpID;
        private bool _gumplingTitleGumpIDDelta;
        private readonly GumpPicTiled _gumpMiddle;
        private readonly GumpPicTiled _gumpRight;
        private readonly GumpPic _gumpTop;
        private bool _isExpanding;
        private readonly bool _isResizable = true;
        private Point _lastExpanderPosition;

        public ExpandableScroll(int x, int y, int height, ushort graphic, bool isResizable = true)
        {
            X = x;
            Y = y;
            SpecialHeight = height;
            _isResizable = isResizable;
            CanMove = true;
            AcceptMouseInput = true;

            int width = 0;

            int w0 = 0,
                w1 = 0,
                w3 = 0;

            for (int i = 0; i < 4; i++)
            {
                ref readonly var gumpInfo = ref Client.Game.UO.Gumps.GetGump((ushort)(graphic + i));

                if (gumpInfo.Texture == null)
                {
                    Dispose();

                    return;
                }

                if (gumpInfo.UV.Width > width)
                {
                    width = gumpInfo.UV.Width;
                }

                if (i == 0)
                {
                    w0 = gumpInfo.UV.Width;
                }
                else if (i == 1)
                {
                    w1 = gumpInfo.UV.Width;
                }
                else if (i == 3)
                {
                    w3 = gumpInfo.UV.Width;
                }
            }

            Add(_gumpTop = new GumpPic(0, 0, graphic, 0));

            Add(_gumpRight = new GumpPicTiled(0, 0, 0, 0, (ushort)(graphic + 1)));

            Add(_gumpMiddle = new GumpPicTiled(0, 0, 0, 0, (ushort)(graphic + 2)));

            Add(_gumpBottom = new GumpPic(0, 0, (ushort)(graphic + 3), 0));

            if (_isResizable)
            {
                Add(
                    _gumpExpander = new Button(c_GumplingExpander_ButtonID, 0x082E, 0x82F)
                    {
                        ButtonAction = ButtonAction.Activate,
                        X = 0,
                        Y = 0
                    }
                );

                _gumpExpander.MouseDown += expander_OnMouseDown;
                _gumpExpander.MouseUp += expander_OnMouseUp;
            }

            int off = w0 - w3;

            _gumpRight.X = _gumpMiddle.X = (width - w1) / 2;
            _gumpRight.Y = _gumpMiddle.Y = _gumplingMidY;
            _gumpRight.Height = _gumpMiddle.Height = _gumplingMidHeight;
            _gumpRight.WantUpdateSize = _gumpMiddle.WantUpdateSize = true;
            _gumpBottom.X = (off / 2) + (off / 4);

            Width = _gumpMiddle.Width;

            WantUpdateSize = true;
        }

        private int _gumplingMidY => _gumpTop.Height;

        private int _gumplingMidHeight =>
            SpecialHeight - _gumpTop.Height - _gumpBottom.Height - (_gumpExpander?.Height ?? 0);

        private int _gumplingBottomY =>
            SpecialHeight - _gumpBottom.Height - (_gumpExpander?.Height ?? 0);

        private int _gumplingExpanderX => (Width - (_gumpExpander?.Width ?? 0)) >> 1;

        private int _gumplingExpanderY =>
            SpecialHeight - (_gumpExpander?.Height ?? 0) - c_GumplingExpanderY_Offset;

        public int TitleGumpID
        {
            set
            {
                _gumplingTitleGumpID = value;
                _gumplingTitleGumpIDDelta = true;
            }
        }

        public int SpecialHeight { get; set; }

        public ushort Hue
        {
            get => _gumpTop.Hue;
            set => _gumpTop.Hue = _gumpBottom.Hue = _gumpMiddle.Hue = _gumpRight.Hue = value;
        }

        public override void Dispose()
        {
            if (_gumpExpander != null)
            {
                _gumpExpander.MouseDown -= expander_OnMouseDown;
                _gumpExpander.MouseUp -= expander_OnMouseUp;
                _gumpExpander.Dispose();
                _gumpExpander = null;
            }

            base.Dispose();
        }

        // IEX: dark windows - the scroll becomes a black stone frame with a gold title plate
        private ResizePic _iexFrame, _iexPlate;
        private Control _iexTitle;

        private void IexDark()
        {
            bool dark = IEX.IexDarkUi.Active;

            if (dark && _iexFrame == null)
            {
                Insert(0, _iexFrame = new ResizePic(0x0E10) { AcceptMouseInput = false });
                string title = IEX.IexDarkUi.ScrollTitle(_gumplingTitleGumpID);

                if (title != null)
                {
                    _iexTitle = IEX.IexDarkUi.TitlePic(title);

                    if (_iexTitle != null)
                    {
                        _iexPlate = new ResizePic(0x0BB8) { AcceptMouseInput = false, Width = _iexTitle.Width + 40, Height = _iexTitle.Height + 10 };
                        Add(_iexPlate);
                        Add(_iexTitle);
                    }
                }
            }

            if (_iexFrame == null)
            {
                return;
            }

            _iexFrame.IsVisible = dark;
            _gumpTop.IsVisible = _gumpMiddle.IsVisible = _gumpRight.IsVisible = _gumpBottom.IsVisible = !dark;

            if (_iexTitle != null)
            {
                _iexTitle.IsVisible = _iexPlate.IsVisible = dark;

                if (_gumplingTitle != null)
                {
                    _gumplingTitle.IsVisible = !dark;
                }
            }

            if (dark)
            {
                IEX.IexDarkUi.KeepReadable(RootParent);
                // Cover everything the parchment covered (its top and bottom rolls are wider than
                // the middle, and windows put text out there), plus a little at the bottom where
                // the roll used to sit under the last line.
                int left = System.Math.Min(0, System.Math.Min(_gumpMiddle.X, _gumpBottom.X));
                int right = System.Math.Max(_gumpTop.Width, System.Math.Max(_gumpMiddle.X + _gumpMiddle.Width, _gumpBottom.X + _gumpBottom.Width));
                _iexFrame.X = left;
                _iexFrame.Y = 0;
                _iexFrame.Width = right - left;
                _iexFrame.Height = _gumplingBottomY + _gumpBottom.Height + 12;

                if (_iexPlate != null)
                {
                    _iexPlate.X = _iexFrame.X + (_iexFrame.Width - _iexPlate.Width) / 2;
                    _iexPlate.Y = 8;
                    _iexTitle.X = _iexPlate.X + 20;
                    _iexTitle.Y = _iexPlate.Y + 5;
                }
            }
        }

        public override bool Contains(int x, int y)
        {
            if (_iexFrame != null && _iexFrame.IsVisible) // IEX: dark windows
            {
                return x >= _iexFrame.X && x < _iexFrame.X + _iexFrame.Width && y >= 0 && y < _iexFrame.Height;
            }

            x += ScreenCoordinateX;
            y += ScreenCoordinateY;

            Control c = null;

            _gumpTop.HitTest(x, y, ref c);

            if (c != null)
            {
                return true;
            }

            _gumpMiddle.HitTest(x, y, ref c);

            if (c != null)
            {
                return true;
            }

            _gumpRight.HitTest(x, y, ref c);

            if (c != null)
            {
                return true;
            }

            _gumpBottom.HitTest(x, y, ref c);

            if (c != null)
            {
                return true;
            }

            _gumpExpander.HitTest(x, y, ref c);

            if (c != null)
            {
                return true;
            }

            return false;
        }

        public override void Update()
        {
            if (Mouse.LButtonPressed && _isExpanding)
            {
                SpecialHeight += Mouse.Position.Y - _lastExpanderPosition.Y;
                _lastExpanderPosition = Mouse.Position;
            }

            if (SpecialHeight < c_ExpandableScrollHeight_Min)
            {
                _lastExpanderPosition.Y += c_ExpandableScrollHeight_Min - SpecialHeight;
                SpecialHeight = c_ExpandableScrollHeight_Min;
            }

            if (SpecialHeight > c_ExpandableScrollHeight_Max)
            {
                _lastExpanderPosition.Y -= SpecialHeight - c_ExpandableScrollHeight_Max;
                SpecialHeight = c_ExpandableScrollHeight_Max;
            }

            if (_gumplingTitleGumpIDDelta)
            {
                _gumplingTitleGumpIDDelta = false;

                _gumplingTitle?.Dispose();
                Add(_gumplingTitle = new GumpPic(0, 0, (ushort)_gumplingTitleGumpID, 0));
            }

            //if (!IsVisible)
            //    IsVisible = true;
            //TOP
            _gumpTop.X = 0;
            _gumpTop.Y = 0;
            _gumpTop.WantUpdateSize = true;
            //MIDDLE
            _gumpRight.Y = _gumpMiddle.Y = _gumplingMidY;
            _gumpRight.Height = _gumpMiddle.Height = _gumplingMidHeight;
            _gumpRight.WantUpdateSize = _gumpMiddle.WantUpdateSize = true;
            //BOTTOM
            _gumpBottom.Y = _gumplingBottomY;
            _gumpBottom.WantUpdateSize = true;

            if (_isResizable)
            {
                _gumpExpander.X = _gumplingExpanderX;
                _gumpExpander.Y = _gumplingExpanderY;
                _gumpExpander.WantUpdateSize = true;
            }

            if (_gumplingTitle != null)
            {
                _gumplingTitle.X = (_gumpTop.Width - _gumplingTitle.Width) >> 1;
                _gumplingTitle.Y = (_gumpTop.Height - _gumplingTitle.Height) >> 1;
                _gumplingTitle.WantUpdateSize = true;
            }

            IexDark(); // IEX: dark windows

            WantUpdateSize = true;
            Parent?.OnPageChanged();

            base.Update();
        }

        private void expander_OnMouseDown(object sender, MouseEventArgs args)
        {
            if (args.Button == MouseButtonType.Left)
            {
                _isExpanding = true;
                _lastExpanderPosition = Mouse.Position;
            }
        }

        private void expander_OnMouseUp(object sender, MouseEventArgs args)
        {
            _isExpanding = false;
        }
    }
}
