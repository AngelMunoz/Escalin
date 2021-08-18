#r "nuget: Microsoft.Playwright, 1.14.0"
#r "nuget: Ply, 0.3.1"

(*
  ==============================================================================
    NOTE: THIS SCRIPT WON'T WORK IF YOU HAVE NOT INSTALLED THE
    PLAYWRIGHT PACKAGES VIA THE GLOBAL DOTNET TOOL MICROSOFT.PLAYWRIGHT.CLI
    OR NPM'S `npx playwright-cli install` ONCE YOU HAVE PLAYWRIGHT BROWSERS
    INSTALLED YOU CAN RUN ANY SCRIPT THAT TARGETS PLAYWRIGHT.
  ==============================================================================
*)


open Microsoft.Playwright
open System.Threading.Tasks
open FSharp.Control.Tasks
open System.IO
open System.Text.Json

type Browser =
    | Chromium
    | Chrome
    | Edge
    | Firefox
    | Webkit
    member this.AsString =
        match this with
        | Chromium -> "Chromium"
        | Chrome -> "Chrome"
        | Edge -> "Edge"
        | Firefox -> "Firefox"
        | Webkit -> "Webkit"

type Post =
    { title: string
      author: string
      summary: string
      tags: string array
      date: string }

let getBrowser (kind: Browser) (pl: Task<IPlaywright>) =
    task {
        let! pl = pl
        printfn $"Browsing with {kind.AsString}"

        return!
            match kind with
            | Chromium -> pl.Chromium.LaunchAsync()
            | Chrome ->
                let opts = BrowserTypeLaunchOptions()
                opts.Channel <- "chrome"
                pl.Chromium.LaunchAsync(opts)
            | Edge ->
                let opts = BrowserTypeLaunchOptions()
                opts.Channel <- "msedge"
                pl.Chromium.LaunchAsync(opts)
            | Firefox -> pl.Firefox.LaunchAsync()
            | Webkit -> pl.Webkit.LaunchAsync()
    }

let getPage (url: string) (browser: Task<IBrowser>) =
    task {
        let! browser = browser
        printfn $"Navigating to \"{url}\""
        let! page = browser.NewPageAsync()
        let! res = page.GotoAsync url

        if not res.Ok then
            // we should be able to handle this part better if
            // we use result instead of just the type
            return failwith "We couldn't navigate to that page"

        return page
    }

let convertElementToPost (element: IElementHandle) =
    task {
        let! headerContent = element.QuerySelectorAsync(".title")
        let! author = element.QuerySelectorAsync(".subtitle a")

        let! content = element.QuerySelectorAsync(".content")

        (* Content looks like
Simple things in F If you come from PHP, Javascript this might help you understand a... #dotnet  #fsharp  #mvc  #saturn \nJul 16, 2021
         *)

        let! title = headerContent.InnerTextAsync()
        let! authorText = author.InnerTextAsync()
        let! rawContent = content.InnerTextAsync()
        // try to split the summary and the tags
        let summaryParts = rawContent.Split("...")

        let summary =
            summaryParts
            |> Array.tryHead
            |> Option.defaultValue ""

        // try to split the tags and the date
        let extraParts =
            (summaryParts
             |> Array.tryLast
             |> Option.defaultValue "\n")
                .Split '\n'

        // split the tags given that each has a '#' and trim it, remove it if it's whitespace

        let tags =
            (extraParts
             |> Array.tryHead
             |> Option.defaultValue "")
                .Split('#')
            |> Array.map (fun s -> s.Trim())
            // filter out those that are not null or whitespace or empty strings
            // equivalent to not(String.IsNullOrWhiteSpace(value))
            |> Array.filter (System.String.IsNullOrWhiteSpace >> not)

        let date =
            extraParts
            |> Array.tryLast
            |> Option.defaultValue ""

        printfn $"Parsed: {title} - {authorText}"

        return
            { title = title
              author = authorText
              tags = tags
              summary = $"{summary}..."
              date = date }
    }

let getPostSummaries (page: Task<IPage>) =

    task {
        let! page = page
        let! cards = page.QuerySelectorAllAsync(".card-content")
        printfn $"Getting Cards from the landing page: {cards.Count}"

        return!
            cards
            |> Seq.toArray
            |> Array.Parallel.map convertElementToPost
            |> Task.WhenAll
    }

let writePostsToFile (posts: Task<Post array>) =
    task {
        let! posts = posts

        let opts =
            let opts = JsonSerializerOptions()
            opts.WriteIndented <- true
            opts

        let json =
            JsonSerializer.SerializeToUtf8Bytes(posts, opts)

        printfn "Saving to \"./posts.json\""
        return! File.WriteAllBytesAsync("./posts.json", json)
    }


[<EntryPoint>]
let main _ =
    Playwright.CreateAsync()
    |> getBrowser Firefox
    |> getPage "https://blog.tunaxor.me"
    |> getPostSummaries
    |> writePostsToFile
    |> Async.AwaitTask
    |> Async.RunSynchronously

    0 // return an integer exit code
