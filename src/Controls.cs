// Controls drawn in the game's style, with the game's own fonts when available.
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ExanimaLauncher
{
    enum TextStyle { Body, Title }

    static class Ui
    {
        // Text size in the game font's cell units; also mapped to a fallback system font size.
        public const float BodySize = 29f, SmallSize = 25f, TabSize = 44f, PlaySize = 58f;

        static GlyphFont FontFor(TextStyle style) { return style == TextStyle.Title ? GameAssets.Title : GameAssets.Body; }

        static Font Fallback(TextStyle style, float size)
        {
            return style == TextStyle.Title ? new Font("Georgia", size * 0.28f, FontStyle.Bold) : new Font("Segoe UI", size * 0.27f);
        }

        public static SizeF Measure(string text, TextStyle style, float size)
        {
            var font = FontFor(style);
            if (font != null) return new SizeF(font.Measure(text, size), size * (style == TextStyle.Title ? 0.62f : 0.7f));
            using (var f = Fallback(style, size)) return TextRenderer.MeasureText(text, f);
        }

        // Draws text left/center/right aligned in bounds, vertically centered.
        public static void Draw(Graphics g, string text, TextStyle style, float size, Color color, RectangleF bounds,
                                StringAlignment align)
        {
            if (string.IsNullOrEmpty(text)) return;
            var font = FontFor(style);
            if (font != null)
            {
                float w = font.Measure(text, size);
                float x = align == StringAlignment.Near ? bounds.X : align == StringAlignment.Center
                        ? bounds.X + (bounds.Width - w) / 2 : bounds.Right - w;
                // the visible glyphs sit in the middle of each cell
                float y = bounds.Y + (bounds.Height - size) / 2 + size * (style == TextStyle.Title ? 0.02f : 0.04f);
                var clip = g.Clip;
                g.SetClip(bounds, CombineMode.Intersect);   // long text is cut at the edge of its box
                font.Draw(g, text, x, y, size, color);
                g.Clip = clip;
                return;
            }
            using (var f = Fallback(style, size))
            {
                var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis |
                            (align == StringAlignment.Center ? TextFormatFlags.HorizontalCenter
                             : align == StringAlignment.Far ? TextFormatFlags.Right : TextFormatFlags.Left);
                TextRenderer.DrawText(g, text, f, Rectangle.Round(bounds), color, flags);
            }
        }

        // Thin double-line frame in the style of the game's panels.
        public static void Frame(Graphics g, Rectangle r, Color color)
        {
            using (var outer = new Pen(Color.FromArgb(150, color)))
            using (var inner = new Pen(Color.FromArgb(60, color)))
            {
                g.DrawRectangle(outer, r.X, r.Y, r.Width - 1, r.Height - 1);
                g.DrawRectangle(inner, r.X + 3, r.Y + 3, r.Width - 7, r.Height - 7);
            }
        }

        // The game's diamond toggle: outline when off, filled when on.
        public static void Diamond(Graphics g, PointF c, float r, bool filled, Color color)
        {
            var pts = new[] { new PointF(c.X, c.Y - r), new PointF(c.X + r, c.Y), new PointF(c.X, c.Y + r), new PointF(c.X - r, c.Y) };
            var old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var pen = new Pen(color, 1.3f)) g.DrawPolygon(pen, pts);
            if (filled)
            {
                float k = r * 0.5f;
                var inner = new[] { new PointF(c.X, c.Y - k), new PointF(c.X + k, c.Y), new PointF(c.X, c.Y + k), new PointF(c.X - k, c.Y) };
                using (var b = new SolidBrush(color)) g.FillPolygon(b, inner);
            }
            g.SmoothingMode = old;
        }
    }

    // Base for transparent, double-buffered controls painted over the background image.
    class GameControl : Control
    {
        protected bool Hover;

        public GameControl()
        {
            SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Color.Transparent;
            ForeColor = Theme.Text;
        }

        protected override void OnMouseEnter(EventArgs e) { Hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { Hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
    }

    class GameText : GameControl
    {
        public TextStyle Style = TextStyle.Body;
        public float TextSize = Ui.BodySize;
        public StringAlignment Align = StringAlignment.Near;
        public bool Multiline;   // word-wrap and honour line breaks, top-aligned

        public GameText() { SetStyle(ControlStyles.Selectable, false); }
        protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (!Multiline)
            {
                Ui.Draw(e.Graphics, Text, Style, TextSize, ForeColor, ClientRectangle, Align);
                return;
            }
            // each line is drawn in a box one glyph cell tall (so nothing is clipped), lines spaced closer
            float lineHeight = TextSize * 0.72f, y = -TextSize * 0.18f;
            foreach (string paragraph in (Text ?? "").Replace("\r", "").Split('\n'))
            {
                string line = "";
                foreach (string word in paragraph.Split(' '))
                {
                    string candidate = line.Length == 0 ? word : line + " " + word;
                    if (line.Length > 0 && Ui.Measure(candidate, Style, TextSize).Width > Width)
                    {
                        Ui.Draw(e.Graphics, line, Style, TextSize, ForeColor, new RectangleF(0, y, Width, TextSize), Align);
                        y += lineHeight;
                        line = word;
                    }
                    else line = candidate;
                }
                Ui.Draw(e.Graphics, line, Style, TextSize, ForeColor, new RectangleF(0, y, Width, TextSize), Align);
                y += lineHeight;
            }
        }
    }

    class GameButton : GameControl
    {
        public bool Primary;          // PLAY: title font, gold
        public bool Tab, Selected;    // tab header: no frame, underline when selected

        public GameButton() { Cursor = Cursors.Hand; }
        protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            var r = ClientRectangle;
            if (Tab)
            {
                Color c = Selected ? Theme.TextBright : Hover ? Theme.Text : Theme.Muted;
                Ui.Draw(g, Text, TextStyle.Title, Ui.TabSize, c, r, StringAlignment.Center);
                if (Selected)
                    using (var pen = new Pen(Color.FromArgb(180, Theme.Accent), 1.5f))
                        g.DrawLine(pen, r.Width * 0.2f, r.Height - 3, r.Width * 0.8f, r.Height - 3);
                return;
            }
            int fill = Primary ? (Hover ? 150 : 110) : (Hover ? 120 : 70);
            using (var b = new SolidBrush(Color.FromArgb(fill, Primary ? Theme.AccentDark : Theme.Panel))) g.FillRectangle(b, r);
            Ui.Frame(g, r, Primary ? Theme.Accent : Theme.Text);
            Color text = !Enabled ? Theme.Disabled : Primary ? Theme.TextBright : Theme.Text;
            if (Primary) Ui.Draw(g, Text, TextStyle.Title, Ui.PlaySize, text, r, StringAlignment.Center);
            else Ui.Draw(g, Text, TextStyle.Body, Ui.BodySize, text, r, StringAlignment.Center);
        }

        protected override void OnClick(EventArgs e) { if (Enabled) base.OnClick(e); }
    }

    class GameCheck : GameControl
    {
        bool isChecked;
        public event EventHandler CheckedChanged;

        public GameCheck() { Cursor = Cursors.Hand; }

        public bool Checked
        {
            get { return isChecked; }
            set
            {
                if (isChecked == value) return;
                isChecked = value;
                Invalidate();
                if (CheckedChanged != null) CheckedChanged(this, EventArgs.Empty);
            }
        }

        protected override void OnClick(EventArgs e) { Checked = !Checked; base.OnClick(e); }
        protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Color c = Hover ? Theme.TextBright : Theme.Text;
            Ui.Diamond(e.Graphics, new PointF(9, Height / 2f), 7, isChecked, c);
            Ui.Draw(e.Graphics, Text, TextStyle.Body, Ui.BodySize, c, new RectangleF(26, 0, Width - 26, Height), StringAlignment.Near);
        }
    }

    class GameSlider : GameControl
    {
        int min, max = 100, value;
        bool dragging;
        public event EventHandler ValueChanged;

        public GameSlider() { Cursor = Cursors.Hand; }

        public int Minimum { get { return min; } set { min = value; Invalidate(); } }
        public int Maximum { get { return max; } set { max = value; Invalidate(); } }

        public int Value
        {
            get { return value; }
            set
            {
                int v = Math.Max(min, Math.Min(max, value));
                if (v == this.value) return;
                this.value = v;
                Invalidate();
                if (ValueChanged != null) ValueChanged(this, EventArgs.Empty);
            }
        }

        float TrackLeft { get { return 10; } }
        float TrackRight { get { return Width - 10; } }

        void SetFromMouse(int x)
        {
            float t = (x - TrackLeft) / Math.Max(1, TrackRight - TrackLeft);
            Value = (int)Math.Round(min + Math.Max(0, Math.Min(1, t)) * (max - min));
        }

        protected override void OnMouseDown(MouseEventArgs e) { dragging = true; SetFromMouse(e.X); base.OnMouseDown(e); }
        protected override void OnMouseMove(MouseEventArgs e) { if (dragging) SetFromMouse(e.X); base.OnMouseMove(e); }
        protected override void OnMouseUp(MouseEventArgs e) { dragging = false; base.OnMouseUp(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            float y = Height / 2f;
            using (var pen = new Pen(Color.FromArgb(120, Theme.Text)))
            {
                g.DrawLine(pen, TrackLeft, y - 3, TrackRight, y - 3);
                g.DrawLine(pen, TrackLeft, y + 3, TrackRight, y + 3);
            }
            float t = max > min ? (value - min) / (float)(max - min) : 0;
            float x = TrackLeft + t * (TrackRight - TrackLeft);
            Ui.Diamond(g, new PointF(x, y), 8, true, Hover || dragging ? Theme.TextBright : Theme.Text);
        }
    }

    // Combo box drawn with the game's body font.
    class GameCombo : ComboBox
    {
        public GameCombo()
        {
            DrawMode = DrawMode.OwnerDrawFixed;
            DropDownStyle = ComboBoxStyle.DropDownList;
            FlatStyle = FlatStyle.Flat;
            BackColor = Theme.Panel;
            ForeColor = Theme.Text;
            ItemHeight = 22;
        }

        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            bool edit = (e.State & DrawItemState.ComboBoxEdit) != 0;
            bool selected = !edit && (e.State & DrawItemState.Selected) != 0;
            using (var b = new SolidBrush(selected ? Theme.Selection : Theme.Panel)) e.Graphics.FillRectangle(b, e.Bounds);
            if (e.Index < 0) return;
            var r = new RectangleF(e.Bounds.X + 6, e.Bounds.Y, e.Bounds.Width - 8, e.Bounds.Height);
            Ui.Draw(e.Graphics, Convert.ToString(Items[e.Index]), TextStyle.Body, Ui.BodySize, selected ? Theme.TextBright : Theme.Text,
                    r, StringAlignment.Near);
        }
    }
}
