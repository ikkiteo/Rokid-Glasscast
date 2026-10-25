using System;
using System.Drawing;
using System.Windows.Forms;

namespace GlassCast
{
    sealed class RegionPick : Form
    {
        Point a, b;
        bool drag;
        public Rectangle Picked = Rectangle.Empty;

        public RegionPick()
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            Bounds = SystemInformation.VirtualScreen;
            BackColor = Color.FromArgb(7, 16, 24);
            Opacity = 0.35;
            TopMost = true;
            Cursor = Cursors.Cross;
            DoubleBuffered = true;
            KeyPreview = true;
            ShowInTaskbar = false;
            Text = Lang.T("зона экрана", "screen region");
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            Bounds = SystemInformation.VirtualScreen;
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); }
            base.OnKeyDown(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            drag = true;
            a = b = PointToScreen(e.Location);
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (!drag) return;
            b = PointToScreen(e.Location);
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (!drag) return;
            drag = false;
            b = PointToScreen(e.Location);
            Rectangle r = Norm(a, b);
            if (r.Width < 32 || r.Height < 32)
            {
                DialogResult = DialogResult.Cancel;
                Close();
                return;
            }
            Picked = r;
            DialogResult = DialogResult.OK;
            Close();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor);
            if (!drag && Picked.IsEmpty)
            {
                using (var p = new Pen(Color.FromArgb(53, 232, 160), 2))
                using (var f = new Font("Consolas", 16f))
                using (var br = new SolidBrush(Color.FromArgb(53, 232, 160)))
                {
                    string t = Lang.T("зажми и выдели зону   Esc — отмена", "drag to pick a region    Esc cancels");
                    Size sz = TextRenderer.MeasureText(t, f);
                    e.Graphics.DrawString(t, f, br, (Width - sz.Width) / 2f, Height / 2f);
                }
                return;
            }
            Rectangle r = RectangleToClient(Norm(a, b));
            using (var br = new SolidBrush(Color.FromArgb(80, 53, 232, 160)))
            using (var p = new Pen(Color.FromArgb(53, 232, 160), 2))
            {
                e.Graphics.FillRectangle(br, r);
                e.Graphics.DrawRectangle(p, r);
            }
        }

        static Rectangle Norm(Point p, Point q)
        {
            int x = Math.Min(p.X, q.X);
            int y = Math.Min(p.Y, q.Y);
            return new Rectangle(x, y, Math.Abs(p.X - q.X), Math.Abs(p.Y - q.Y));
        }
    }
}
