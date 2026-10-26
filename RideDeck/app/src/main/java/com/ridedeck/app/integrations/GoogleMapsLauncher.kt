package com.ridedeck.app.integrations

import android.content.ActivityNotFoundException
import android.content.Context
import android.content.Intent
import android.net.Uri
import com.ridedeck.core.model.GeoPoint
import java.util.Locale

/**
 * Optional Google Maps hand-off. RideDeck stays open source; when the rider turns this on, the
 * destination is passed to the Google Maps app, which gives its own voice turn-by-turn cycling
 * directions. Uses public intents only, so no API key is needed.
 */
object GoogleMapsLauncher {
    const val PACKAGE = "com.google.android.apps.maps"

    fun isInstalled(context: Context): Boolean =
        context.packageManager.getLaunchIntentForPackage(PACKAGE) != null

    /** Starts cycling navigation; falls back to the Maps website (which offers the app) if needed. */
    fun navigate(context: Context, destination: GeoPoint) {
        val coords = String.format(Locale.US, "%.6f,%.6f", destination.latitude, destination.longitude)
        val nav = Intent(Intent.ACTION_VIEW, Uri.parse("google.navigation:q=$coords&mode=b"))
            .setPackage(PACKAGE)
            .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
        try {
            context.startActivity(nav)
        } catch (e: ActivityNotFoundException) {
            val web = Uri.parse("https://www.google.com/maps/dir/").buildUpon()
                .appendQueryParameter("api", "1")
                .appendQueryParameter("destination", coords)
                .appendQueryParameter("travelmode", "bicycling")
                .build()
            context.startActivity(Intent(Intent.ACTION_VIEW, web).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK))
        }
    }

    fun openStore(context: Context) = openStoreListing(context, PACKAGE)
}

/** Opens an app's store listing in whatever store is installed, or on the web. */
fun openStoreListing(context: Context, packageName: String) {
    try {
        context.startActivity(
            Intent(Intent.ACTION_VIEW, Uri.parse("market://details?id=$packageName")).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK),
        )
    } catch (e: ActivityNotFoundException) {
        context.startActivity(
            Intent(Intent.ACTION_VIEW, Uri.parse("https://play.google.com/store/apps/details?id=$packageName"))
                .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK),
        )
    }
}
