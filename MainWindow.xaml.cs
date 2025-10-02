using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace BmpRgb565Viewer;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private BitmapSource? _originalBitmap;
    private BitmapSource? _convertedBitmap;

    public MainWindow()
    {
        InitializeComponent();
    }

    private void OnClickOpen(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new OpenFileDialog
            {
                Filter = "BMP Files (*.bmp)|*.bmp|All Files (*.*)|*.*",
                Title = "BMP 열기"
            };
            if (dialog.ShowDialog() == true)
            {
                using var stream = File.OpenRead(dialog.FileName);
                var decoder = new BmpBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                _originalBitmap = decoder.Frames[0];
                OriginalImage.Source = _originalBitmap;
                _convertedBitmap = null;
                ConvertedImage.Source = null;
                StatusText.Text = $"불러옴: {System.IO.Path.GetFileName(dialog.FileName)} ({_originalBitmap.PixelWidth}x{_originalBitmap.PixelHeight}, {_originalBitmap.Format})";
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "열기 실패", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnClickConvert(object sender, RoutedEventArgs e)
    {
        if (_originalBitmap == null)
        {
            MessageBox.Show(this, "먼저 BMP를 열어주세요.", "안내", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            bool isBigEndian = BigEndianCheckBox.IsChecked == true;
            _convertedBitmap = ConvertToRgb565(_originalBitmap, isBigEndian);
            ConvertedImage.Source = _convertedBitmap;
            StatusText.Text = $"RGB565 변환 완료 ({(isBigEndian ? "빅엔디안" : "리틀엔디안")})";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "변환 실패", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnClickSave(object sender, RoutedEventArgs e)
    {
        //if (_convertedBitmap == null)
        //{
        //    MessageBox.Show(this, "먼저 RGB565로 변환하세요.", "안내", MessageBoxButton.OK, MessageBoxImage.Information);
        //    return;
        //}
        //try
        //{
        //    bool isBigEndian = BigEndianCheckBox.IsChecked == true;
        //    var dialog = new SaveFileDialog
        //    {
        //        Filter = "Binary Files (*.bin)|*.bin|All Files (*.*)|*.*",
        //        Title = "RGB565 RAW 로우데이터 저장",
        //        FileName = $"converted_rgb565_{(isBigEndian ? "be" : "le")}.bin"
        //    };
        //    if (dialog.ShowDialog() == true)
        //    {
        //        var rgb565 = EnsureBgr565(_convertedBitmap);
        //        var (pixels16, stride16, width, height) = ExtractBgr565(rgb565);
        //        // stride를 무시하고 유효 픽셀만 행 순서대로 저장
        //        var raw = new byte[width * height * 2];
        //        for (int y = 0; y < height; y++)
        //        {
        //            Buffer.BlockCopy(pixels16, y * stride16, raw, y * width * 2, width * 2);
        //        }
        //        File.WriteAllBytes(dialog.FileName, raw);
        //        StatusText.Text = $"RAW 저장됨: {System.IO.Path.GetFileName(dialog.FileName)} ({raw.Length} bytes, {(isBigEndian ? "빅엔디안" : "리틀엔디안")})";
        //    }
        //}
        //catch (Exception ex)
        //{
        //    MessageBox.Show(this, ex.Message, "저장 실패", MessageBoxButton.OK, MessageBoxImage.Error);
        //}

        bool isBigEndian = BigEndianCheckBox.IsChecked == true;

        var dialog = new SaveFileDialog
        {
            Filter =
                "C 헤더 (*.h)|*.h|" +
                "C 소스 (*.c)|*.c|" +
                "헥사 텍스트 (*.txt)|*.txt|" +
                "바이너리 (*.bin)|*.bin|" +
                "모든 파일 (*.*)|*.*",
            Title = "RGB565 RAW 저장",
            FileName = $"converted_rgb565_{(isBigEndian ? "be" : "le")}.h"
        };

        if (dialog.ShowDialog() == true)
        {
            // 1) RGB565 픽셀 버퍼 추출
            var rgb565 = EnsureBgr565(_convertedBitmap);
            var (pixels16, strideBytes, width, height) = ExtractBgr565(rgb565); // stride는 바이트 단위라 가정

            // 2) 행 패딩(stride) 무시하고 유효 픽셀만 1D 순서로 뽑기
            //    - ushort 픽셀 값 목록 (엔디언 해석 포함)
            List<ushort> pxU16 = new(width * height);
            for (int y = 0; y < height; y++)
            {
                int row = y * strideBytes;
                for (int x = 0; x < width; x++)
                {
                    int i = row + x * 2;
                    ushort v = isBigEndian
                        ? (ushort)((pixels16[i] << 8) | pixels16[i + 1])   // BE: hi, lo
                        : (ushort)(pixels16[i] | (pixels16[i + 1] << 8));  // LE: lo, hi
                    pxU16.Add(v);
                }
            }

            // 3) 포맷별 저장
            string ext = System.IO.Path.GetExtension(dialog.FileName).ToLowerInvariant();
            string baseName = System.IO.Path.GetFileNameWithoutExtension(dialog.FileName);

            if (ext == ".h" || ext == ".c")
            {
                // unsigned short C 배열 출력
                string varName = MakeSafeCIdent(baseName);
                string code = BuildCUnsignedShortArray(
                    varName, pxU16, width, height, isBigEndian ? "BE" : "LE", 12 /*words per line*/);
                File.WriteAllText(dialog.FileName, code, Encoding.ASCII);
            }
            else if (ext == ".txt")
            {
                // ── unsigned short 헥사 값(0xXXXX) 콤마 구분 텍스트 ──
                var sb = new StringBuilder();
                for (int i = 0; i < pxU16.Count; i++)
                {
                    sb.Append("0x").Append(pxU16[i].ToString("X4"));
                    if (i != pxU16.Count - 1) sb.Append(", ");
                    if ((i + 1) % 12 == 0) sb.AppendLine(); // 가독성을 위해 12개 단위 줄바꿈
                }
                File.WriteAllText(dialog.FileName, sb.ToString(), Encoding.ASCII);
            }
            else // .bin
            {
                // RAW 바이너리 (엔디언에 맞춘 바이트열)
                byte[] rawBytes = new byte[pxU16.Count * 2];
                for (int i = 0; i < pxU16.Count; i++)
                {
                    ushort v = pxU16[i];
                    if (isBigEndian)
                    {
                        rawBytes[2 * i] = (byte)(v >> 8);
                        rawBytes[2 * i + 1] = (byte)(v & 0xFF);
                    }
                    else
                    {
                        rawBytes[2 * i] = (byte)(v & 0xFF);
                        rawBytes[2 * i + 1] = (byte)(v >> 8);
                    }
                }
                File.WriteAllBytes(dialog.FileName, rawBytes);
            }

            StatusText.Text =
                $"저장됨: {System.IO.Path.GetFileName(dialog.FileName)} " +
                $"({width * height * 2} bytes, {(isBigEndian ? "빅엔디안" : "리틀엔디안")}, " +
                $"{ext.ToUpperInvariant()} 포맷)";
        }

    }

    private void OnClickSaveRle(object sender, RoutedEventArgs e)
    {
        if (_convertedBitmap == null)
        {
            MessageBox.Show(this, "먼저 RGB565로 변환하세요.", "안내", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        try
        {
            bool isBigEndian = BigEndianCheckBox.IsChecked == true;
            bool isRle = RLECheckBox.IsChecked == true; // RLE 여부만 유지

            var dialog = new SaveFileDialog
            {
                Filter =
                    "C 헤더 파일 (*.h)|*.h|" +
                    "C 소스 파일 (*.c)|*.c|" +
                    "헥사 텍스트 (*.txt;*.csv)|*.txt;*.csv|" +
                    "바이너리 (*.bin)|*.bin|" +
                    "모든 파일 (*.*)|*.*",
                Title = "RGB565 RLE/RAW 저장 (unsigned short / 워드헥사 / 바이너리)",
                FileName = $"converted_rgb565_{(isRle ? "rle" : "raw")}_{(isBigEndian ? "be" : "le")}.h"
            };

            if (dialog.ShowDialog() == true)
            {
                // 1) 픽셀 추출
                var rgb565 = EnsureBgr565(_convertedBitmap);
                var (pixels16, strideBytes, width, height) = ExtractBgr565(rgb565); // stride는 '바이트' 단위

                // 2) 데이터 생성 (전역/라인 기반, 엔디언)
                //    주의: 아래 두 인코더는 payload를 '리틀엔디언 워드 시퀀스'로 기록한다고 가정
                byte[] payload = isRle
                    ? EncodeRleRgb565(pixels16, strideBytes, width, height, isBigEndian)          // 라인 기반 RLE
                    : EncodeRleRgb565Global(pixels16, strideBytes, width, height, isBigEndian);   // 전역 RLE

                // 3) 확장자별 저장 (항상 unsigned short/워드 기준)
                string ext = System.IO.Path.GetExtension(dialog.FileName).ToLowerInvariant();
                string varBase = System.IO.Path.GetFileNameWithoutExtension(dialog.FileName)
                                    .Replace('-', '_').Replace(' ', '_');
                string varName = MakeSafeCIdent(varBase);

                if (ext == ".h" || ext == ".c")
                {
                    // ── unsigned short 배열(.h/.c)
                    // payload를 16비트 워드(LE)로 해석해서 상수 배열로 출력
                    string cCode = BuildCUnsignedShortArrayFromBytes(
                        varName: varName,
                        dataBytes: payload,
                        isRleFormat: isRle,
                        endianLabel: isBigEndian ? "BE" : "LE", // 원본 입력 바이트 순서 메모용
                        wordsPerLine: 12,
                        littleEndianWords: true                 // payload는 <ushort> LE 시퀀스임
                    );
                    File.WriteAllText(dialog.FileName, cCode, Encoding.ASCII);
                }
                else if (ext == ".txt" || ext == ".csv")
                {
                    // ── 16비트 워드 헥사 텍스트 (0xXXXX, 0xXXXX, ...)
                    string hexWords = BuildCommaSeparatedHexWords(
                        dataBytes: payload,
                        littleEndianWords: true, // payload는 LE 워드 시퀀스
                        wordsPerLine: 12
                    );
                    File.WriteAllText(dialog.FileName, hexWords, Encoding.ASCII);
                }
                else
                {
                    // ── 바이너리 그대로
                    File.WriteAllBytes(dialog.FileName, payload);
                }

                StatusText.Text = $"저장됨: {System.IO.Path.GetFileName(dialog.FileName)} " +
                    $"({payload.Length} bytes, {(isBigEndian ? "빅엔디안" : "리틀엔디안")} {(isRle ? ", 라인기반" : ", 전체기반")}, " +
                    (ext == ".h" || ext == ".c" ? "unsigned short 배열" : (ext == ".txt" || ext == ".csv" ? "워드 헥사 텍스트" : "바이너리")) + ")";
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "저장 실패", MessageBoxButton.OK, MessageBoxImage.Error);
        }

    }

    private static BitmapSource ConvertToRgb565(BitmapSource source, bool isBigEndian = false)
    {
        // 1) 32bpp로 정규화
        var normalized = source.Format == PixelFormats.Bgra32
            ? source
            : new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);

        int width = normalized.PixelWidth;
        int height = normalized.PixelHeight;
        int stride32 = (width * 32 + 7) / 8;
        byte[] pixels32 = new byte[stride32 * height];
        normalized.CopyPixels(pixels32, stride32, 0);

        // 2) 16bpp RGB565 버퍼 구성
        int stride16 = (width * 16 + 31) / 32 * 4; // BMP stride 규칙과 무관, WPF용 stride
        byte[] pixels16 = new byte[stride16 * height];

        for (int y = 0; y < height; y++)
        {
            int srcRow = y * stride32;
            int dstRow = y * stride16;
            for (int x = 0; x < width; x++)
            {
                int si = srcRow + x * 4; // BGRA32
                byte b = pixels32[si + 0];
                byte g = pixels32[si + 1];
                byte r = pixels32[si + 2];
                // A는 무시

                ushort r5 = (ushort)(r >> 3);
                ushort g6 = (ushort)(g >> 2);
                ushort b5 = (ushort)(b >> 3);
                ushort rgb565 = (ushort)((r5 << 11) | (g6 << 5) | b5);

                int di = dstRow + x * 2;
                if (isBigEndian)
                {
                    // 빅엔디안: 상위 바이트 먼저
                    pixels16[di + 0] = (byte)(rgb565 >> 8);
                    pixels16[di + 1] = (byte)(rgb565 & 0xFF);
                }
                else
                {
                    // 리틀엔디안: 하위 바이트 먼저
                    pixels16[di + 0] = (byte)(rgb565 & 0xFF);
                    pixels16[di + 1] = (byte)(rgb565 >> 8);
                }
            }
        }

        // 3) WPF BitmapSource 생성 (Bgr565)
        var bmp565 = BitmapSource.Create(
            width,
            height,
            normalized.DpiX,
            normalized.DpiY,
            PixelFormats.Bgr565,
            null,
            pixels16,
            stride16);
        bmp565.Freeze();
        return bmp565;
    }

    // BMP 저장은 요구 변경으로 제거

    private static BitmapSource EnsureBgr565(BitmapSource src)
    {
        return src.Format == PixelFormats.Bgr565 ? src : new FormatConvertedBitmap(src, PixelFormats.Bgr565, null, 0);
    }

    private static (byte[] pixels16, int stride16, int width, int height) ExtractBgr565(BitmapSource bmp565)
    {
        int width = bmp565.PixelWidth;
        int height = bmp565.PixelHeight;
        int stride16 = (width * 16 + 31) / 32 * 4;
        byte[] pixels16 = new byte[stride16 * height];
        bmp565.CopyPixels(pixels16, stride16, 0);
        return (pixels16, stride16, width, height);
    }

    // 단순 RLE: 같은 16비트 픽셀 반복을 (count, value)로 저장. 행 단위로 리셋.
    // 포맷: [width(2), height(2)] 헤더 + 각 행 [런(count1(2), value1(2)), ..., 0x0000(2) EOL] 형태
    // count는 1..65535, value는 RGB565 (엔디안에 따라 다름)
    private static byte[] EncodeRleRgb565(byte[] pixels16, int stride16, int width, int height, bool isBigEndian = false)
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        bw.Write((ushort)width);
        bw.Write((ushort)height);

        for (int y = 0; y < height; y++)
        {
            int rowStart = y * stride16;
            int x = 0;
            while (x < width)
            {
                int di = rowStart + x * 2;
                ushort value;
                if (isBigEndian)
                {
                    // 빅엔디안: 상위 바이트 먼저
                    value = (ushort)((pixels16[di] << 8) | pixels16[di + 1]);
                }
                else
                {
                    // 리틀엔디안: 하위 바이트 먼저
                    value = (ushort)(pixels16[di] | (pixels16[di + 1] << 8));
                }
                int run = 1;

                // 다음 픽셀들과 동일 값 반복 계산
                while (x + run < width)
                {
                    int di2 = rowStart + (x + run) * 2;
                    ushort value2;
                    if (isBigEndian)
                    {
                        value2 = (ushort)((pixels16[di2] << 8) | pixels16[di2 + 1]);
                    }
                    else
                    {
                        value2 = (ushort)(pixels16[di2] | (pixels16[di2 + 1] << 8));
                    }
                    if (value2 != value) break;
                    run++;
                    if (run == 0xFFFF) break; // ushort 최대
                }

                bw.Write((ushort)run);
                bw.Write(value);
                x += run;
            }
            // 행 종료 마커 (0)
            bw.Write((ushort)0);
        }
        return ms.ToArray();
    }

    private static byte[] EncodeRleRgb565Global(
    byte[] pixels16, int strideBytes, int width, int height, bool isBigEndian = false)
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        bool hasPrev = false;
        ushort prev = 0;
        int run = 0;

        for (int y = 0; y < height; y++)
        {
            int rowStart = y * strideBytes; // 주의: stride는 "바이트" 단위
            for (int x = 0; x < width; x++)
            {
                int di = rowStart + x * 2;
                ushort value = isBigEndian
                    ? (ushort)((pixels16[di] << 8) | pixels16[di + 1])   // MSB, LSB
                    : (ushort)(pixels16[di] | (pixels16[di + 1] << 8));  // LSB, MSB

                if (!hasPrev)
                {
                    prev = value;
                    run = 1;
                    hasPrev = true;
                }
                else if (value == prev && run < 0xFFFF)
                {
                    run++;
                }
                else
                {
                    bw.Write((ushort)run);
                    bw.Write(prev);
                    prev = value;
                    run = 1;
                }
            }
            // 전역 RLE: 행 종료 마커 쓰지 않음
        }

        if (hasPrev)
        {
            bw.Write((ushort)run);
            bw.Write(prev);
        }

        return ms.ToArray();
    }

    private static string BuildCArrayCode(
    string varName,
    byte[] data,
    int width,
    int height,
    bool isRle,
    string endianLabel,
    int bytesPerLine = 16)
    {
        var sb = new StringBuilder();
        sb.AppendLine("/* Auto-generated from RGB565 data */");
        sb.AppendLine("#include <stdint.h>");
        sb.AppendLine();
        sb.AppendLine($"#define IMG_WIDTH   {width}");
        sb.AppendLine($"#define IMG_HEIGHT  {height}");
        sb.AppendLine($"#define IMG_FORMAT  {(isRle ? "RLE" : "RAW")}  /* payload format */");
        sb.AppendLine($"#define IMG_ENDIAN  {endianLabel}               /* BE or LE */");
        sb.AppendLine($"#define IMG_SIZE    {data.Length}               /* bytes */");
        sb.AppendLine();
        sb.AppendLine($"const uint8_t {varName}[] = {{");

        for (int i = 0; i < data.Length; i++)
        {
            if (i % bytesPerLine == 0) sb.Append("  ");
            sb.Append("0x").Append(data[i].ToString("X2"));
            if (i != data.Length - 1) sb.Append(", ");
            if ((i + 1) % bytesPerLine == 0) sb.AppendLine();
        }
        if (data.Length % bytesPerLine != 0) sb.AppendLine();
        sb.AppendLine("};");
        return sb.ToString();
    }

    private static string BuildCommaSeparatedHex(byte[] data, int bytesPerLine = 16)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < data.Length; i++)
        {
            sb.Append("0x").Append(data[i].ToString("X2"));
            if (i != data.Length - 1) sb.Append(", ");
            if ((i + 1) % bytesPerLine == 0) sb.AppendLine();
        }
        return sb.ToString();
    }

    private static string BuildCUnsignedShortArray(
    string varName,
    IReadOnlyList<ushort> data,
    int width, int height,
    string endianLabel,
    int wordsPerLine = 12)
    {
        var sb = new StringBuilder();
        sb.AppendLine("/* Auto-generated RGB565 data */");
        sb.AppendLine("#include <stdint.h>");
        sb.AppendLine();
        sb.AppendLine($"#define IMG_WIDTH   {width}");
        sb.AppendLine($"#define IMG_HEIGHT  {height}");
        sb.AppendLine($"#define IMG_ENDIAN  {endianLabel}   /* BE or LE source order */");
        sb.AppendLine($"#define IMG_PIXELS  {data.Count}");
        sb.AppendLine();
        sb.AppendLine($"const unsigned short {varName}[IMG_PIXELS] = {{");

        for (int i = 0; i < data.Count; i++)
        {
            if (i % wordsPerLine == 0) sb.Append("  ");
            sb.Append("0x").Append(data[i].ToString("X4"));
            if (i != data.Count - 1) sb.Append(", ");
            if ((i + 1) % wordsPerLine == 0) sb.AppendLine();
        }
        if (data.Count % wordsPerLine != 0) sb.AppendLine();
        sb.AppendLine("};");
        return sb.ToString();
    }

    private static string MakeSafeCIdent(string name)
    {
        var sb = new StringBuilder();
        if (string.IsNullOrEmpty(name) || !(char.IsLetter(name[0]) || name[0] == '_')) sb.Append('_');
        foreach (char c in name)
            sb.Append(char.IsLetterOrDigit(c) || c == '_' ? c : '_');
        return sb.ToString();
    }

    private static string BuildCommaHexBytes(byte[] bytes, int bytesPerLine = 16)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < bytes.Length; i++)
        {
            sb.Append("0x").Append(bytes[i].ToString("X2"));
            if (i != bytes.Length - 1) sb.Append(", ");
            if ((i + 1) % bytesPerLine == 0) sb.AppendLine();
        }
        return sb.ToString();
    }
    // dataBytes를 16비트 워드들(LE)로 해석해 unsigned short 배열 C 코드를 생성
    // - isRleFormat: 주석에 RLE/RAW 표기를 위한 메타
    // - littleEndianWords: dataBytes가 [lo,hi] 순서(LE)로 워드들이 나열됐으면 true
    private static string BuildCUnsignedShortArrayFromBytes(
        string varName,
        byte[] dataBytes,
        bool isRleFormat,
        string endianLabel,
        int wordsPerLine = 12,
        bool littleEndianWords = true)
    {
        if (dataBytes.Length % 2 != 0)
            throw new ArgumentException("dataBytes length must be even for uint16 array.");

        int words = dataBytes.Length / 2;

        var sb = new StringBuilder();
        sb.AppendLine("/* Auto-generated from RGB565 data */");
        sb.AppendLine("#include <stdint.h>");
        sb.AppendLine($"#define IMG_FORMAT  {(isRleFormat ? "RLE" : "RAW")}  /* payload format */");
        sb.AppendLine($"#define IMG_ENDIAN  {endianLabel}   /* BE or LE (source) */");
        sb.AppendLine($"#define IMG_WORDS   {words}         /* uint16 elements */");
        sb.AppendLine();
        sb.AppendLine($"const unsigned short {varName}[IMG_WORDS] = {{");

        for (int i = 0; i < words; i++)
        {
            int bi = i * 2;
            ushort w = littleEndianWords
                ? (ushort)(dataBytes[bi] | (dataBytes[bi + 1] << 8))    // LE: lo, hi
                : (ushort)((dataBytes[bi] << 8) | dataBytes[bi + 1]);   // BE: hi, lo

            if (i % wordsPerLine == 0) sb.Append("  ");
            sb.Append("0x").Append(w.ToString("X4"));
            if (i != words - 1) sb.Append(", ");
            if ((i + 1) % wordsPerLine == 0) sb.AppendLine();
        }
        if (words % wordsPerLine != 0) sb.AppendLine();
        sb.AppendLine("};");
        return sb.ToString();
    }
    private static string BuildCommaSeparatedHexWords(
    byte[] dataBytes, bool littleEndianWords = true, int wordsPerLine = 12)
    {
        if (dataBytes.Length % 2 != 0)
            throw new ArgumentException("dataBytes length must be even for uint16 words.");

        var sb = new StringBuilder();
        int words = dataBytes.Length / 2;

        for (int i = 0; i < words; i++)
        {
            int bi = i * 2;
            ushort w = littleEndianWords
                ? (ushort)(dataBytes[bi] | (dataBytes[bi + 1] << 8))
                : (ushort)((dataBytes[bi] << 8) | dataBytes[bi + 1]);

            sb.Append("0x").Append(w.ToString("X4"));
            if (i != words - 1) sb.Append(", ");
            if ((i + 1) % wordsPerLine == 0) sb.AppendLine();
        }
        return sb.ToString();
    }


}