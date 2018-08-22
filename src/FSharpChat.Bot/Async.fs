namespace FSharpChat.Bot

[<AutoOpen>]
module Async = 
    let inline Map (f: 'T -> 'TResult) (operation: Async<'T>) : Async<'TResult> = 
        async {
            let! result = operation
            return f result
        }

    let inline AsAsync value : Async<'T> = 
        async {
            return value
        }