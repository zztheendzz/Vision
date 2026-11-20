using HalconDotNet;
using System;
using System.Collections.Generic;
using System.Text;
using Vision.Core;
using Vision.Geometry;
using Vision.Tools.Bases;
using Vision.Tools.Registry;

namespace Vision.Tools.Locate
{
    /// <summary>Loại mã 2D cần đọc.</summary>
    public enum CodeType
    {
        QrCode,
        DataMatrix,
        Pdf417,
        Aztec,
        MicroQr
    }

    /// <summary>Độ kỹ khi tìm mã: Normal nhanh, Enhanced chậm hơn nhưng đọc được mã mờ, méo, tương phản kém.</summary>
    public enum CodeEffort
    {
        Normal,
        Enhanced
    }

    /// <summary>
    /// Đọc mã 2D (QR, Data Matrix, PDF417, Aztec, Micro QR) bằng Halcon.
    ///  - Input Image (xám). Nối thêm Region (ví dụ từ SelectByRoi) để chỉ tìm trong vùng đó.
    ///  - Output Text = nội dung mã đầu tiên đọc được; Texts = mọi mã đọc được ngăn bằng dấu '|'; Pose = tâm mã đầu tiên; Count = số mã.
    ///  - Không đọc được mã nào là NoResult (NG). Đặt Expected thì nội dung phải CHỨA chuỗi này (không phân biệt hoa thường),
    ///    sai nội dung cũng là NoResult.
    /// </summary>
    [VisionTool("ReadCode", "Read Code (QR / DataMatrix)", Group = "Đọc mã",
                Description = "Đọc mã QR, Data Matrix, PDF417, Aztec và trả nội dung")]
    public class ReadCodeTool : PureToolBase
    {
        public override IReadOnlyList<PortDef> Inputs { get; } = new[]
        {
            new PortDef("Image",  typeof(HImage)),
            new PortDef("Region", typeof(HRegion), Required: false)
        };

        public override IReadOnlyList<PortDef> Outputs { get; } = new[]
        {
            new PortDef("Text",  typeof(string)),
            new PortDef("Texts", typeof(string)),
            new PortDef("Pose",  typeof(Pose2D)),
            new PortDef("Count", typeof(int))
        };

        public override IReadOnlyList<ParamDef> Parameters { get; } = new[]
        {
            new ParamDef("CodeType",   typeof(CodeType),   CodeType.QrCode, Description: "Loại mã cần đọc"),
            new ParamDef("Effort",     typeof(CodeEffort), CodeEffort.Normal, Description: "Normal = nhanh; Enhanced = chậm hơn, đọc được mã mờ / méo / tương phản thấp"),
            new ParamDef("MaxCodes",   typeof(int),        1, Min: 1, Max: 100, Description: "Số mã tối đa cần tìm trong ảnh"),
            new ParamDef("TimeoutMs",  typeof(int),        500, Min: 10, Max: 60000, Description: "Thời gian tìm tối đa (ms)"),
            new ParamDef("Polarity",   typeof(string),     "any", Description: "any = tự nhận; dark_on_light = mã tối trên nền sáng; light_on_dark = mã sáng trên nền tối"),
            new ParamDef("Expected",   typeof(string),     "", Description: "Để trống = không kiểm. Có giá trị: nội dung mã phải chứa chuỗi này, không thì NG")
        };

        protected override void ValidateRules(VisionToolInput p, List<string> errors)
        {
            string pol = (p.Get<string>("Polarity") ?? "").Trim();
            if (pol is not ("any" or "dark_on_light" or "light_on_dark"))
                errors.Add("Polarity phải là any, dark_on_light hoặc light_on_dark");
        }

        protected override VisionToolResult Execute(VisionToolInput input, ToolContext context)
        {
            var image = input.Get<HImage>("Image");                       // chỉ mượn
            int channels = image.CountChannels();
            if (channels != 1)
                return VisionToolResult.ConfigError($"ReadCode cần ảnh 1 kênh (xám), ảnh nhận vào có {channels} kênh. Thêm tool RgbToGray phía trước.");

            string symbol = input.Get<CodeType>("CodeType") switch
            {
                CodeType.QrCode => "QR Code",
                CodeType.DataMatrix => "Data Matrix ECC 200",
                CodeType.Pdf417 => "PDF417",
                CodeType.Aztec => "Aztec Code",
                _ => "Micro QR Code"
            };
            int max = input.Get<int>("MaxCodes");
            int timeout = input.Get<int>("TimeoutMs");
            string polarity = input.Get<string>("Polarity").Trim();
            bool enhanced = input.Get<CodeEffort>("Effort") == CodeEffort.Enhanced;
            string expected = (input.Get<string>("Expected") ?? "").Trim();

            HImage? limited = null;
            HDataCode2D? model = null;
            HXLDCont? contours = null;
            try
            {
                var search = image;
                if (input.TryGet<HRegion>("Region", out var region) && region != null && region.IsInitialized())
                {
                    if (region.CountObj() == 0 || region.AreaCenter(out double _, out double _) < 1)
                        return VisionToolResult.NoResult("Region đầu vào rỗng, không có vùng để đọc mã");
                    limited = image.ReduceDomain(region);
                    search = limited;
                }

                model = new HDataCode2D(symbol, new HTuple(), new HTuple());
                model.SetDataCode2dParam("default_parameters", enhanced ? "enhanced_recognition" : "standard_recognition");
                model.SetDataCode2dParam("timeout", timeout);
                if (polarity != "any") model.SetDataCode2dParam("polarity", polarity);

                contours = model.FindDataCode2d(search, new HTuple("stop_after_result_num"), new HTuple(max),
                                                out HTuple handles, out HTuple strings);

                var texts = new List<string>();
                for (int i = 0; i < strings.Length; i++) texts.Add(strings[i].S);

                if (texts.Count == 0)
                {
                    contours.Dispose();
                    contours = null;
                    return VisionToolResult.NoResult($"Không đọc được mã {symbol} nào (thử Effort = Enhanced, đổi Polarity, hoặc kiểm tra ánh sáng / lấy nét)");
                }

                if (expected.Length > 0 && !texts.Any(t => t.Contains(expected, StringComparison.OrdinalIgnoreCase)))
                {
                    contours.Dispose();
                    contours = null;
                    return VisionToolResult.NoResult($"Đọc được '{string.Join(" | ", texts)}' nhưng không chứa '{expected}'");
                }

                Pose2D? pose = null;
                using (var first = contours.SelectObj(1))
                {
                    first.AreaCenterXld(out HTuple rows, out HTuple cols, out _);
                    if (rows.Length > 0) pose = new Pose2D(rows[0].D, cols[0].D, 0.0);
                }

                var result = VisionToolResult.Ok($"{symbol}: {texts.Count} mã, '{texts[0]}'")
                    .Add("Text", texts[0])
                    .Add("Texts", string.Join("|", texts))
                    .Add("Count", texts.Count);
                if (pose != null) result.Add("Pose", pose.Value);
                result.Values["Text"] = texts[0];
                result.Values["Count"] = texts.Count.ToString();

                if (context.NeedDisplay) { result.AddDisplay(contours); contours = null; }   // giao cho result
                return result;
            }
            finally
            {
                contours?.Dispose();
                model?.Dispose();
                limited?.Dispose();
            }
        }
    }
}
