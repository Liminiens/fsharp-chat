namespace FSharpChat.Bot

[<AutoOpen>]
module Common =
    let inline isNotNull obj =
        isNull obj |> not

