using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

internal static class IconBuilder
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Length < 2)
        {
            return;
        }

        using (Bitmap bitmap = CreateIconBitmap())
        {
            bitmap.Save(args[0], ImageFormat.Png);

            using (MemoryStream pngStream = new MemoryStream())
            {
                bitmap.Save(pngStream, ImageFormat.Png);
                byte[] pngBytes = pngStream.ToArray();

                using (FileStream file = File.Create(args[1]))
                using (BinaryWriter writer = new BinaryWriter(file))
                {
                    writer.Write((short)0);
                    writer.Write((short)1);
                    writer.Write((short)1);
                    writer.Write((byte)0);
                    writer.Write((byte)0);
                    writer.Write((byte)0);
                    writer.Write((byte)0);
                    writer.Write((short)1);
                    writer.Write((short)32);
                    writer.Write(pngBytes.Length);
                    writer.Write(22);
                    writer.Write(pngBytes);
                }
            }
        }
    }

    private static Bitmap CreateIconBitmap()
    {
        Bitmap bitmap = new Bitmap(256, 256, PixelFormat.Format32bppArgb);

        using (Graphics graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.Clear(Color.Transparent);

            using (GraphicsPath backgroundPath = CreateRoundedRectangle(
                new Rectangle(8, 8, 240, 240),
                48))
            using (SolidBrush backgroundBrush = new SolidBrush(
                Color.FromArgb(31, 43, 64)))
            {
                graphics.FillPath(backgroundBrush, backgroundPath);
            }

            using (SolidBrush pageBrush = new SolidBrush(Color.FromArgb(246, 248, 250)))
            using (Pen outlinePen = new Pen(Color.FromArgb(198, 207, 218), 5F))
            {
                Rectangle leftPage = new Rectangle(50, 70, 78, 126);
                Rectangle rightPage = new Rectangle(128, 70, 78, 126);

                graphics.FillRectangle(pageBrush, leftPage);
                graphics.FillRectangle(pageBrush, rightPage);
                graphics.DrawRectangle(outlinePen, leftPage);
                graphics.DrawRectangle(outlinePen, rightPage);
            }

            using (Pen linePen = new Pen(Color.FromArgb(143, 155, 172), 6F))
            {
                linePen.StartCap = LineCap.Round;
                linePen.EndCap = LineCap.Round;

                graphics.DrawLine(linePen, 68, 102, 108, 102);
                graphics.DrawLine(linePen, 68, 128, 108, 128);
                graphics.DrawLine(linePen, 148, 102, 188, 102);
                graphics.DrawLine(linePen, 148, 128, 188, 128);
                graphics.DrawLine(linePen, 148, 154, 188, 154);
            }

            using (Pen maskPen = new Pen(Color.FromArgb(72, 200, 136), 27F))
            {
                maskPen.StartCap = LineCap.Round;
                maskPen.EndCap = LineCap.Round;
                maskPen.LineJoin = LineJoin.Round;
                graphics.DrawLine(maskPen, 42, 171, 211, 151);
            }
        }

        return bitmap;
    }

    private static GraphicsPath CreateRoundedRectangle(
        Rectangle bounds,
        int radius)
    {
        int diameter = radius * 2;
        GraphicsPath path = new GraphicsPath();
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(
            bounds.Right - diameter,
            bounds.Top,
            diameter,
            diameter,
            270,
            90);
        path.AddArc(
            bounds.Right - diameter,
            bounds.Bottom - diameter,
            diameter,
            diameter,
            0,
            90);
        path.AddArc(
            bounds.Left,
            bounds.Bottom - diameter,
            diameter,
            diameter,
            90,
            90);
        path.CloseFigure();
        return path;
    }
}
