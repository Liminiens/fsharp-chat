namespace FSharpChat.Bot

open System

module Media = 
    open ImageMagick

    let getMimeType (data: byte[]) =
        try
            let formatInfo = 
                MagickFormatInfo.Create(MagickImageInfo(data).Format)
            Some(formatInfo.MimeType)
        with
        | :? Exception as e ->
            None
