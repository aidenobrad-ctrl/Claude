# Publishing RideDeck on Google Play

The app is ready for Play: it targets Android 16 (API 36), asks for no background location, and CI builds a signed app bundle (`.aab`) once you give it your own upload key. These are the steps only you can do, in order.

## 1. Create a developer account

1. Go to [play.google.com/console](https://play.google.com/console) and sign up. There is a one-time US$25 fee, and Google verifies your identity, which can take a few days.
2. Choose a **personal** account unless you have a registered business.

New personal accounts must run a **closed test with at least 12 testers for 14 days** before Google lets you publish to everyone. Start step 6 as early as you can.

## 2. Make your upload key

The CI key in `ci/` is public, so never use it for Play. On a computer with Java installed, run:

```
keytool -genkeypair -v -keystore ridedeck-upload.jks -alias upload -keyalg RSA -keysize 4096 -validity 10000
```

Pick a strong password and keep `ridedeck-upload.jks` and the password somewhere safe, such as a password manager. With Play App Signing (the default), Google holds the real app signing key. If you lose the upload key, you can ask Google to reset it.

## 3. Give the key to the build

In the GitHub repository, open **Settings → Secrets and variables → Actions** and add four secrets:

| Secret | Value |
| --- | --- |
| `RIDEDECK_KEYSTORE_BASE64` | The keystore as base64: `base64 -w0 ridedeck-upload.jks` (macOS: `base64 -i ridedeck-upload.jks`) |
| `RIDEDECK_KEYSTORE_PASSWORD` | The keystore password |
| `RIDEDECK_KEY_ALIAS` | `upload` |
| `RIDEDECK_KEY_PASSWORD` | The key password (the same as the keystore password unless you chose another) |

Then re-run the **RideDeck APK** workflow (Actions → RideDeck APK → Run workflow). Download `RideDeck-aab` from the run's artifacts. To get a release with both files, push a tag such as `ridedeck-v1.1.0`.

From then on the APK is signed with your key as well. Your phone will refuse to install it over the old CI-signed build, so uninstall RideDeck once first. Your rides are stored on the phone, so uninstalling deletes them.

Every build gets a higher version code automatically, which Play requires for each upload.

## 4. Create the app in Play Console

**Create app** → name `RideDeck`, App, Free. Then work through **Dashboard → Set up your app**:

- **Privacy policy:** `https://github.com/aidenobrad-ctrl/Claude/blob/main/RideDeck/PRIVACY.md`. This works once the RideDeck branch is merged into main. Add your contact email to it if you like.
- **App access:** all features are available without special access.
- **Ads:** no ads.
- **Content rating:** fill in the questionnaire. It's a utility with no user content, so it should get "Everyone".
- **Target audience:** 13 and over.
- **Data safety.** Here are suggested answers; check them against the current form:
  - Data is collected: **Location → Precise location**. It is used for app functionality, is optional for some features, is not shared, and is processed ephemerally where it is sent for directions or search.
  - **Audio files**, for the music player: processed on the device only, so this usually doesn't count as collected. If the form insists, declare it as app functionality, not shared.
  - Data is encrypted in transit: yes (HTTPS).
  - Users can request deletion: yes, by deleting rides in the app or uninstalling.
  - No account, so there's no account deletion to describe.
- **Foreground service permissions.** Declare both:
  - *Location*: "Records a bike ride the user started, with a visible notification, while the screen is off."
  - *Media playback*: "Plays the user's music in the background."

  Google asks for a short video of each. A screen recording on your phone works: start a ride, turn the screen off, show the notification, then play music and leave the app.
- **News app, COVID, government, financial features:** no to all.

## 5. Store listing

**Grow → Store presence → Main store listing:**

- **Text:** copy it from [`store/listing.md`](../store/listing.md).
- **App icon:** [`store/icon-512.png`](../store/icon-512.png).
- **Feature graphic:** [`store/feature-graphic.png`](../store/feature-graphic.png).
- **Phone screenshots:** take at least 2 on your phone, ideally 4 to 8. Good ones are:
  - the Ride screen with map, speedometer and music;
  - the speedometer on its own;
  - navigation with steps;
  - History;
  - Settings → Ride screen views.

## 6. Closed test, then production

1. **Testing → Closed testing → Create track.** Upload `RideDeck.aab`, add release notes, then add at least 12 testers by email or Google Group. They join through the opt-in link and need to keep the app installed.
2. After 14 days with 12 testers, **apply for production access** from the Dashboard and answer Google's questions about the test.
3. **Production → Create new release**, upload the latest `.aab`, and roll out. The first review usually takes a few days.

## Things to know

- **Google map:** if you add a `MAPS_API_KEY`, add the **app signing key SHA-1** to the key's restrictions in Google Cloud too. You'll find it under Play Console → Test and release → App integrity. The upload key SHA-1 isn't enough, because Play re-signs the app.
- **Spotify control** uses notification access. Play allows this, but say so in the description, which `store/listing.md` already does.
- **OpenStreetMap tiles:** the public OSM tile servers are fine for a small app. If RideDeck gets popular, switch to a tile provider with a free tier (MapTiler, Stadia, Thunderforest), because OSM's usage policy forbids heavy use.
