using System;
using System.Collections.Generic;
using System.Text;
using Vision.Core;
using Vision.Graph;
using Vision.Execution;


namespace Vision.Graph
{
        public static class GraphDemo
        {
            public static void Run(string imagePath)
            {
                using var graph = new VisionGraph();

                var load = graph.AddNode("LoadImageTool");
                var thr = graph.AddNode("ThresholdTool");
                var blobA = graph.AddNode("BlobTool");
                var blobB = graph.AddNode("BlobTool");

                // Tham số "thiết kế" của node (lưu vào file graph)
                load.SetParameter("Path", imagePath);
                thr.SetParameter("Max", 100.0);
                blobA.SetParameter("MinArea", 500.0);
                blobB.SetParameter("MinArea", 5000.0);

                graph.Connect(load.Output("Image"), thr.Input("Image"));
                graph.Connect(thr.Output("Region"), blobA.Input("Region"));
                graph.Connect(thr.Output("Region"), blobB.Input("Region"));

                var exec = new GraphExecutor();
                exec.NodeFinished += (n, r) =>
                    System.Diagnostics.Debug.WriteLine($"{n.ToolName,-10} {r.Status,-8} {r.ElapsedMs} ms  {r.Message}");

                // 1) Chạy với tham số trong node
                using (var run = exec.Run(graph))
                System.Diagnostics.Debug.WriteLine($"A = {run[blobA].Result!.Get<int>("Count")}, B = {run[blobB].Result!.Get<int>("Count")}");

                // 2) Người ngoài truyền tham số cho riêng lần chạy này (node không bị đổi)
                var opts = new RunOptions()
                    .SetParameter(thr, "Max", 80.0)
                    .SetParameter(blobA, "MinArea", 2000.0)
                    .SetParameter(blobA, "MaxArea", 50000.0);

                using (var run = exec.Run(graph, opts))
                System.Diagnostics.Debug.WriteLine($"[override] A = {run[blobA].Result!.Get<int>("Count")}");

            System.Diagnostics.Debug.WriteLine($"MinArea của blobA vẫn là {blobA.GetParameter<double>("MinArea")}");

                // 3) Đưa ảnh từ ngoài vào thay cho LoadImage: seed "Image" + bỏ node LoadImage
                //    var o = new RunOptions().SetSeed("Image", hImageFromCamera);   // seed chỉ mượn, không Dispose

                // 4) Thử sai: ném ArgumentException ngay khi truyền
                try { new RunOptions().SetParameter(thr, "Min", 999.0); }
                catch (ArgumentException ex) { Console.WriteLine("Bị chặn: " + ex.Message); }

                // 5) Kiểm tra rò rỉ
                for (int i = 0; i < 1000; i++)
                {
                    using var r = exec.Run(graph);
                }
            System.Diagnostics.Debug.WriteLine("Done 1000 runs");
            }
        }
    
}
