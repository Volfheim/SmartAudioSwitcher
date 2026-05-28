using System;
using System.Drawing;
using System.IO;

namespace IconGen
{
    class Program
    {
        static void Main(string[] args)
        {
            try 
            {
                using (var bitmap = new Bitmap(64, 64))
                {
                    using (var g = Graphics.FromImage(bitmap))
                    {
                        g.Clear(Color.Transparent);
                        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                        g.FillEllipse(Brushes.CornflowerBlue, 2, 2, 60, 60);
                        g.DrawEllipse(new Pen(Color.White, 4), 10, 10, 44, 44);
                        
                        // Draw a "play" triangle
                        Point[] points = { new Point(24, 20), new Point(44, 32), new Point(24, 44) };
                        g.FillPolygon(Brushes.White, points);
                    }
                    
                    // Create an icon handle
                    var hIcon = bitmap.GetHicon();
                    using (var icon = System.Drawing.Icon.FromHandle(hIcon))
                    {
                        using (var fs = new FileStream("app.ico", FileMode.Create))
                        {
                            icon.Save(fs);
                        }
                    }
                }
                Console.WriteLine("Icon created successfully.");
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error: " + ex.Message);
            }
        }
    }
}
