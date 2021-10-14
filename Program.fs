// Learn more about F# at http://docs.microsoft.com/dotnet/fsharp

open FSharp.Data
open FSharp.Text.RegexProvider
open FSharpPlus
open System
open System.Globalization
open FSharp.Control.Reactive
open FSharpPlus.Data
open System.Reactive.Concurrency

type HouseList = HtmlProvider<"data/list.html", PreferOptionals=true>
type HouseListing = HtmlProvider<"data/house.html", PreferOptionals=true>

type BedroomRegex = Regex< @"(?<Bedrooms>\d+).*bedroom" >
type BathRegex = Regex< @"(?<Baths>\d+)\s+bath" >
type RentRegex = Regex< @"\$(?<Rent>[\d,]+)" >

let DEBUG = false

module Helpers =
    let output x = Writer.tell <| DList.ofSeq [ x ]

    let log x = if DEBUG then printfn $"###{x}"


    let loggedCall f x =
        log x
        f x

    let listToTuple4 x =
        match List.length x with
        | l when l >= 4 -> Some(x.[0], x.[1], x.[2], x.[3])
        | _ -> None

    let logAndContinue x =
        x
        |> Observable.choose ((Result.mapError (tap log)) >> Option.ofResult)


open Helpers

let houseListLoader =
    loggedCall (fun x -> HouseList.AsyncLoad(uri = x) |> Observable.ofAsync)

let listingLoader =
    loggedCall
        (fun x ->
            HouseListing.AsyncLoad(uri = x)
            |> Observable.ofAsync)

let loadPage x =
    let url =
        $"https://www.bpmco.com/view-all-pre-leasing-properties/page/{x}/"

    url
    |> houseListLoader
    |> Observable.map (fun x -> x, url)

let collectLinks ((x: HouseList), url) =
    try
        x.Lists.``Search-results``.Html.CssSelect("a")
        |> Observable.ofSeq
        |> Observable.map
            (fun x ->
                x.TryGetAttribute("href")
                |> Option.map (fun x -> x.Value()))
        |> Observable.map (Option.toResultWith $"Failed to get href in {url}")
        |> logAndContinue
    with
    | :? System.Collections.Generic.KeyNotFoundException ->
        log $"{url} did not match the specifications"
        Observable.empty

let isProperty x = String.isSubString "/property/" x

let cellsExtract x =
    let loader = listingLoader x

    let cells =
        loader
        |> Observable.map (fun x -> (x, x.Html.CssSelect(".available-for-pre-leasing")))

    Observable.zip
        (cells
         |> Observable.map
             (fun (loaded, _) ->
                 x,
                 loaded.Lists.``Details:``.Values
                 |> List.ofArray
                 |> listToTuple4
                 |> Option.toResultWith $"Unexpected # of details in {x}"))
        (cells
         |> Observable.flatmap (snd >> Observable.ofSeq))

let extractInfo ((uri, details), (x: HtmlNode)) =
    let parseData id f getter caster opName =
        x.CssSelect(id)
        |> List.tryHead
        |> Option.toResultWith (Exception $"Could not find any item of id {id}")
        |>> (fun x -> x.InnerText())
        |>> f
        |>> getter
        |>> Result.protect caster
        |> Result.flatten


    uri,
    details,
    monad {
        let! rentOpt =
            x.CssSelect(".unit-rent")
            |> List.tryHead
            |> Option.toResultWith (Exception $"Could not find element with class unit-rent")

        let! bedrooms =
            parseData
                ".unit-beds-baths"
                (fun x -> BedroomRegex().TypedMatch(x))
                (fun x -> x.Bedrooms.Value)
                int
                "bedrooms"

        let! baths =
            parseData ".unit-beds-baths" (fun x -> BathRegex().TypedMatch(x)) (fun x -> x.Baths.Value) int "baths"

        let! unitId = parseData ".unit-number" id id id "unitId"

        let! rent =
            parseData
                ".unit-rent"
                (fun x -> RentRegex().TypedMatch(x))
                (fun x -> x.Rent.Value)
                (fun x -> Int32.Parse(s = x, style = NumberStyles.AllowThousands))
                "rent"

        return unitId, bedrooms, baths, rent
    }

let printer =
    (fun (uri, (location, neighborhood, pets, zoning), (unit, bedrooms, baths, rent)) ->
        printfn $"{uri},{unit},{bedrooms},{baths},{location},{neighborhood},{pets},{zoning},{rent}")

let logFailed (x, details, y) =
    match (y, details) with
    | Ok z, Ok d -> Some(x, d, z)
    | _ ->
        if DEBUG then
            printfn $"###{x} needs manually adding"

        None

[<EntryPoint>]
let main argv =

    let scheduler = NewThreadScheduler()

    [ 1 .. 24 ]
    |> Observable.ofSeqOn scheduler
    |> Observable.flatmap loadPage
    |> Observable.flatmap collectLinks
    |> Observable.filter isProperty
    |> Observable.distinct
    |> Observable.flatmap (cellsExtract >> Observable.map extractInfo)
    |> Observable.choose logFailed
    |> Observable.perform printer
    |> Observable.wait
    |> ignore

    0
