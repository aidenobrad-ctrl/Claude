package com.ridedeck.app.ui.map

import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.ArrowUpward
import androidx.compose.material.icons.filled.Flag
import androidx.compose.material.icons.filled.ForkLeft
import androidx.compose.material.icons.filled.ForkRight
import androidx.compose.material.icons.filled.Merge
import androidx.compose.material.icons.filled.NearMe
import androidx.compose.material.icons.filled.RampLeft
import androidx.compose.material.icons.filled.RampRight
import androidx.compose.material.icons.filled.RoundaboutLeft
import androidx.compose.material.icons.filled.RoundaboutRight
import androidx.compose.material.icons.filled.Straight
import androidx.compose.material.icons.filled.TurnLeft
import androidx.compose.material.icons.filled.TurnRight
import androidx.compose.material.icons.filled.TurnSharpLeft
import androidx.compose.material.icons.filled.TurnSharpRight
import androidx.compose.material.icons.filled.TurnSlightLeft
import androidx.compose.material.icons.filled.TurnSlightRight
import androidx.compose.material.icons.filled.UTurnLeft
import androidx.compose.material.icons.filled.UTurnRight
import androidx.compose.ui.graphics.vector.ImageVector

/** Maps maneuver codes (see `OsrmApi.maneuverCode`) to icons. */
fun maneuverIcon(maneuver: String?): ImageVector = when (maneuver) {
    "TURN_LEFT" -> Icons.Filled.TurnLeft
    "TURN_RIGHT" -> Icons.Filled.TurnRight
    "TURN_SLIGHT_LEFT" -> Icons.Filled.TurnSlightLeft
    "TURN_SLIGHT_RIGHT" -> Icons.Filled.TurnSlightRight
    "TURN_SHARP_LEFT" -> Icons.Filled.TurnSharpLeft
    "TURN_SHARP_RIGHT" -> Icons.Filled.TurnSharpRight
    "UTURN_LEFT" -> Icons.Filled.UTurnLeft
    "UTURN_RIGHT" -> Icons.Filled.UTurnRight
    "STRAIGHT" -> Icons.Filled.Straight
    "RAMP_LEFT" -> Icons.Filled.RampLeft
    "RAMP_RIGHT" -> Icons.Filled.RampRight
    "MERGE" -> Icons.Filled.Merge
    "FORK_LEFT" -> Icons.Filled.ForkLeft
    "FORK_RIGHT" -> Icons.Filled.ForkRight
    "ROUNDABOUT_LEFT" -> Icons.Filled.RoundaboutLeft
    "ROUNDABOUT_RIGHT" -> Icons.Filled.RoundaboutRight
    "DEPART" -> Icons.Filled.NearMe
    "ARRIVE" -> Icons.Filled.Flag
    "NAME_CHANGE" -> Icons.Filled.ArrowUpward
    "FERRY", "FERRY_TRAIN" -> Icons.Filled.Flag
    else -> Icons.Filled.ArrowUpward
}
