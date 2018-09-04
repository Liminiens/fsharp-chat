#nowarn "0067"
namespace FSharpChat.Bot
open System

module Media = 
    open System.IO
    open SixLabors.ImageSharp    
    open SixLabors.ImageSharp.Processing
    open SixLabors.ImageSharp.Formats.Jpeg
    open ImageMagick
    
    let getMimeType (data: byte[]) =
        try
            let formatInfo = 
                MagickFormatInfo.Create(MagickImageInfo(data).Format)
            Ok(formatInfo.MimeType)
        with
        | :? Exception as e ->
            Error("Failed to get mime-type", e)

    type JpegImage = { Content: byte[]; MimeType: string; }

    let toJpeg (data: byte[]) =
        try
            use image = Image.Load(data)
            let stream = new MemoryStream()
            let encoder = new JpegEncoder()
            encoder.Quality <- 85
            image.SaveAsJpeg(stream, encoder)
            { Content = stream.ToArray(); MimeType = "image/jpeg" }
            |> Ok
        with
        | :? Exception as e ->
            Error("Failed to convert image to jpeg", e)
    
    type ResizedImage = { Content: byte[]; Width: int; Height: int; Resized: bool; }

    let resize (width: int, height: int) (data: byte[]) =
        try
            use image = Image.Load(data)
            let format = Image.DetectFormat(data)
            if image.Width > width && image.Height > height then   
                image.Mutate(fun i -> i.Resize(width, height) |> ignore)
                let stream = new MemoryStream()
                image.Save(stream, format)
                { Content = stream.ToArray(); Width = width; Height = height; Resized = true }
                |> Ok
            else
                { Content = data; Width = image.Width; Height = image.Height; Resized = false }
                |> Ok
        with
        | :? Exception as e ->
            Error("Failed to resize image", e)

