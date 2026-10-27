# Releasing

Releases are cut from the **Release** workflow (`.github/workflows/release.yml`). It tests, packs, pushes to nuget.org and creates the GitHub Release, in that order.

## One-time setup

The release uses [NuGet trusted publishing](https://learn.microsoft.com/nuget/nuget-org/trusted-publishing), so no long-lived API key is stored in GitHub.

1. On nuget.org, sign in and open your username → **Trusted Publishing**. Add a policy with:
   - **Repository owner:** the GitHub owner of this repository.
   - **Repository:** the repository name.
   - **Workflow file:** `release.yml` (the file name only).
   - **Environment:** leave empty.
   - **Scope:** allow publishing new packages and new versions, for the glob `TechnicalDogsbody.Optimizely.ContentSeeder*`.
2. In the GitHub repository, add the repository secret `NUGET_USER` (Settings → Secrets and variables → Actions), set to your nuget.org profile name. It is not your email address.
3. Once a release has published successfully, you can delete any old `NUGET_API_KEY` secret and the API key it held on nuget.org.

## Releasing a version

1. Merge the work to `main`, and wait for **CI** to pass.
2. Make sure the committed UI is current: run `npm run build` in `src/TechnicalDogsbody.Optimizely.ContentSeeder/UI/client` and commit `UI/wwwroot` if anything changed. The release fails if it is stale.
3. Go to **Actions → Release → Run workflow** on `main`.
4. Enter the version, such as `1.0.0`, or `1.0.0-beta.1` for a prerelease. Tick **prerelease** for a prerelease.
5. The workflow checks the version, runs the tests, packs both packages, pushes them to nuget.org and creates the `vX.Y.Z` tag and GitHub Release with the `.nupkg` and `.snupkg` files attached.

The workflow refuses to run from anything other than `main`, and refuses a version whose tag already exists. A failed run can be rerun safely.

## Optimizely NuGet feed

The Optimizely feed is not published by this workflow. To make a release available there, download the `.nupkg` files from the GitHub Release and upload them through the same manual process used for Feature Flagging.

Only the `.nupkg` files are needed for the Optimizely feed. The `.snupkg` files are for symbols on nuget.org.
