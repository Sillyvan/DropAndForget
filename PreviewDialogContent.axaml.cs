using System;
using System.IO;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using DropAndForget.Models;
using DropAndForget.ViewModels;
using PDFiumCore;

namespace DropAndForget;

public partial class PreviewDialogContent : UserControl
{
    private const int PdfBitmapFormatBgra = 4;
    private const int PdfRenderAnnotations = 0x01;

    private static readonly object PdfiumLock = new();
    private static bool s_pdfiumInitialized;

    private Bitmap? _bitmap;
    private Image? _pdfImage;
    private GCHandle _pdfBytesHandle;
    private FpdfDocumentT? _pdfDocument;
    private int _pdfPageCount;
    private int _pdfPageNumber;

    public PreviewDialogContent()
    {
        InitializeComponent();
        PreviewHost.SizeChanged += PreviewHost_SizeChanged;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        LoadPreview();
    }

    private void LoadPreview()
    {
        DisposePreviewResources();
        PreviewHost.Content = null;
        PdfPageTextBlock.Text = string.Empty;

        if (DataContext is not PreviewDialogViewModel viewModel)
        {
            return;
        }

        switch (viewModel.Preview.Kind)
        {
            case FilePreviewKind.Image:
                LoadImagePreview(viewModel.Preview.BinaryContent);
                break;
            case FilePreviewKind.Text:
                LoadTextPreview(viewModel.Preview.TextContent ?? string.Empty);
                break;
            case FilePreviewKind.Pdf:
                LoadPdfPreview(viewModel.Preview.BinaryContent);
                break;
        }
    }

    private void LoadImagePreview(byte[]? imageBytes)
    {
        if (imageBytes is null)
        {
            return;
        }

        using var stream = new MemoryStream(imageBytes, writable: false);
        _bitmap = new Bitmap(stream);
        PreviewHost.Content = new Image
        {
            Source = _bitmap,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };
    }

    private void LoadTextPreview(string text)
    {
        PreviewHost.Content = new ScrollViewer
        {
            Padding = new Thickness(16),
            Content = new SelectableTextBlock
            {
                Text = text,
                TextWrapping = TextWrapping.NoWrap,
                FontFamily = FontFamily.Parse("Menlo, Consolas, monospace")
            }
        };
    }

    private void LoadPdfPreview(byte[]? pdfBytes)
    {
        if (pdfBytes is null)
        {
            return;
        }

        EnsurePdfiumInitialized();

        _pdfBytesHandle = GCHandle.Alloc(pdfBytes, GCHandleType.Pinned);
        _pdfDocument = fpdfview.FPDF_LoadMemDocument64(_pdfBytesHandle.AddrOfPinnedObject(), (ulong)pdfBytes.Length, null);
        if (_pdfDocument is null)
        {
            _pdfBytesHandle.Free();
            return;
        }

        _pdfPageCount = fpdfview.FPDF_GetPageCount(_pdfDocument);
        _pdfPageNumber = 0;

        _pdfImage = new Image
        {
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };

        PreviewHost.Content = _pdfImage;
        TryRenderPdfPage();
        Dispatcher.UIThread.Post(TryRenderPdfPage, DispatcherPriority.Loaded);
    }

    private void PreviousPageButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_pdfDocument is null || _pdfPageNumber <= 0)
        {
            return;
        }

        _pdfPageNumber -= 1;
        TryRenderPdfPage();
    }
    
    private void NextPageButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_pdfDocument is null || _pdfPageNumber >= _pdfPageCount - 1)
        {
            return;
        }

        _pdfPageNumber += 1;
        TryRenderPdfPage();
    }

    private void TryRenderPdfPage()
    {
        if (_pdfImage is null || _pdfDocument is null)
        {
            PdfPageTextBlock.Text = string.Empty;
            return;
        }

        if (PreviewHost.Bounds.Width < 1 || PreviewHost.Bounds.Height < 1)
        {
            PdfPageTextBlock.Text = $"Page {_pdfPageNumber + 1} of {_pdfPageCount}";
            return;
        }

        var page = fpdfview.FPDF_LoadPage(_pdfDocument, _pdfPageNumber);
        if (page is null)
        {
            PdfPageTextBlock.Text = $"Page {_pdfPageNumber + 1} of {_pdfPageCount}";
            return;
        }

        try
        {
            var pageWidth = fpdfview.FPDF_GetPageWidthF(page);
            var pageHeight = fpdfview.FPDF_GetPageHeightF(page);
            var widthZoom = PreviewHost.Bounds.Width / pageWidth;
            var heightZoom = PreviewHost.Bounds.Height / pageHeight;
            var zoom = Math.Max(0.1, Math.Min(widthZoom, heightZoom));
            var pixelWidth = Math.Max(1, (int)Math.Ceiling(pageWidth * zoom));
            var pixelHeight = Math.Max(1, (int)Math.Ceiling(pageHeight * zoom));

            var pdfBitmap = fpdfview.FPDFBitmapCreateEx(pixelWidth, pixelHeight, PdfBitmapFormatBgra, IntPtr.Zero, 0);
            if (pdfBitmap is null)
            {
                PdfPageTextBlock.Text = $"Page {_pdfPageNumber + 1} of {_pdfPageCount}";
                return;
            }

            try
            {
                _bitmap?.Dispose();

                _ = fpdfview.FPDFBitmapFillRect(pdfBitmap, 0, 0, pixelWidth, pixelHeight, 0xFFFFFFFF);
                fpdfview.FPDF_RenderPageBitmap(pdfBitmap, page, 0, 0, pixelWidth, pixelHeight, 0, PdfRenderAnnotations);

                var stride = fpdfview.FPDFBitmapGetStride(pdfBitmap);
                var pixels = new byte[stride * pixelHeight];
                Marshal.Copy(fpdfview.FPDFBitmapGetBuffer(pdfBitmap), pixels, 0, pixels.Length);

                var handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
                try
                {
                    _bitmap = new WriteableBitmap(
                        PixelFormat.Bgra8888,
                        AlphaFormat.Unpremul,
                        handle.AddrOfPinnedObject(),
                        new PixelSize(pixelWidth, pixelHeight),
                        new Vector(96, 96),
                        stride);
                }
                finally
                {
                    handle.Free();
                }
            }
            finally
            {
                fpdfview.FPDFBitmapDestroy(pdfBitmap);
            }

            _pdfImage.Source = _bitmap;
            PdfPageTextBlock.Text = $"Page {_pdfPageNumber + 1} of {_pdfPageCount}";
        }
        finally
        {
            fpdfview.FPDF_ClosePage(page);
        }
    }

    private static void EnsurePdfiumInitialized()
    {
        lock (PdfiumLock)
        {
            if (s_pdfiumInitialized)
            {
                return;
            }

            fpdfview.FPDF_InitLibrary();
            s_pdfiumInitialized = true;
        }
    }

    private void PreviewHost_SizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (_pdfDocument is not null && e.NewSize.Width > 0 && e.NewSize.Height > 0)
        {
            TryRenderPdfPage();
        }
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        DisposePreviewResources();
    }

    private void DisposePreviewResources()
    {
        _bitmap?.Dispose();
        _bitmap = null;

        _pdfImage = null;

        if (_pdfDocument is not null)
        {
            fpdfview.FPDF_CloseDocument(_pdfDocument);
            _pdfDocument = null;
        }

        if (_pdfBytesHandle.IsAllocated)
        {
            _pdfBytesHandle.Free();
        }

        _pdfPageCount = 0;
        _pdfPageNumber = 0;
    }
}
