package com.ridedeck.app.ui.map

import android.content.Context
import android.graphics.Color
import android.graphics.drawable.Drawable
import android.graphics.drawable.GradientDrawable
import androidx.compose.runtime.Composable
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.remember
import androidx.compose.ui.platform.LocalContext
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.LifecycleEventObserver
import androidx.lifecycle.compose.LocalLifecycleOwner
import com.ridedeck.core.model.GeoPoint
import org.osmdroid.tileprovider.tilesource.ITileSource
import org.osmdroid.views.CustomZoomButtonsController
import org.osmdroid.views.MapView
import org.osmdroid.views.overlay.TilesOverlay
import org.osmdroid.util.GeoPoint as OsmPoint

/**
 * An osmdroid [MapView] (open-source OpenStreetMap renderer) tied to the composition's lifecycle,
 * so tile downloads pause with the screen and the view is detached when it leaves the composition.
 */
@Composable
fun rememberOsmMapView(): MapView {
    val context = LocalContext.current
    val mapView = remember {
        MapView(context).apply {
            setMultiTouchControls(true)
            zoomController.setVisibility(CustomZoomButtonsController.Visibility.NEVER)
            isTilesScaledToDpi = true
            minZoomLevel = 3.0
            maxZoomLevel = 20.0
            isHorizontalMapRepetitionEnabled = true
            isVerticalMapRepetitionEnabled = false
            controller.setZoom(3.0)
        }
    }
    val lifecycleOwner = LocalLifecycleOwner.current
    DisposableEffect(lifecycleOwner, mapView) {
        val observer = LifecycleEventObserver { _, event ->
            when (event) {
                Lifecycle.Event.ON_RESUME -> mapView.onResume()
                Lifecycle.Event.ON_PAUSE -> mapView.onPause()
                else -> Unit
            }
        }
        lifecycleOwner.lifecycle.addObserver(observer)
        if (lifecycleOwner.lifecycle.currentState.isAtLeast(Lifecycle.State.RESUMED)) mapView.onResume()
        onDispose {
            lifecycleOwner.lifecycle.removeObserver(observer)
            mapView.onPause()
            mapView.onDetach()
        }
    }
    return mapView
}

/** Applies a tile style; dark themes invert the light OSM tiles, which reads well at night. */
fun MapView.applyStyle(source: ITileSource, dark: Boolean) {
    if (tileProvider.tileSource.name() != source.name()) setTileSource(source)
    overlayManager.tilesOverlay.setColorFilter(if (dark) TilesOverlay.INVERT_COLORS else null)
    overlayManager.tilesOverlay.loadingBackgroundColor = if (dark) Color.BLACK else Color.LTGRAY
    overlayManager.tilesOverlay.loadingLineColor = if (dark) Color.DKGRAY else Color.GRAY
}

fun GeoPoint.toOsm(): OsmPoint = OsmPoint(latitude, longitude)

fun OsmPoint.toCore(): GeoPoint = GeoPoint(latitude, longitude)

/** A round marker icon with a white ring, sized in dp. */
fun dotIcon(context: Context, color: Int, diameterDp: Int = 22): Drawable {
    val px = (diameterDp * context.resources.displayMetrics.density).toInt()
    val ring = (3 * context.resources.displayMetrics.density).toInt()
    return GradientDrawable().apply {
        shape = GradientDrawable.OVAL
        setColor(color)
        setStroke(ring, Color.WHITE)
        setSize(px, px)
        setBounds(0, 0, px, px)
    }
}
