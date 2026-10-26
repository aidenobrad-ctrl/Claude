package com.ridedeck.app.ui.history

import androidx.lifecycle.SavedStateHandle
import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.ridedeck.app.data.local.LifetimeTotals
import com.ridedeck.app.data.local.RoutePointEntity
import com.ridedeck.app.data.local.SessionEntity
import com.ridedeck.app.data.prefs.UserPreferencesRepository
import com.ridedeck.app.data.session.SessionRepository
import com.ridedeck.core.model.ActivityMode
import com.ridedeck.core.model.SpeedUnit
import dagger.hilt.android.lifecycle.HiltViewModel
import kotlinx.coroutines.ExperimentalCoroutinesApi
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.SharingStarted
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.combine
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.flow.flatMapLatest
import kotlinx.coroutines.flow.stateIn
import kotlinx.coroutines.launch
import javax.inject.Inject

/** History filter. Starts on the current activity mode, as the mode drives the default filter. */
enum class HistoryFilter { ALL, BIKE, WALK }

data class HistoryUiState(
    val loading: Boolean = true,
    val filter: HistoryFilter = HistoryFilter.ALL,
    val sessions: List<SessionEntity> = emptyList(),
    val totals: LifetimeTotals = LifetimeTotals.EMPTY,
    val unit: SpeedUnit = SpeedUnit.KMH,
)

@OptIn(ExperimentalCoroutinesApi::class)
@HiltViewModel
class HistoryViewModel @Inject constructor(
    private val sessionRepository: SessionRepository,
    preferencesRepository: UserPreferencesRepository,
) : ViewModel() {

    private val filter = MutableStateFlow<HistoryFilter?>(null)

    init {
        viewModelScope.launch {
            val mode = preferencesRepository.mode.first()
            if (filter.value == null) filter.value = if (mode == ActivityMode.BIKE) HistoryFilter.BIKE else HistoryFilter.WALK
        }
    }

    private val sessions = filter.flatMapLatest { f ->
        sessionRepository.observeSessions(
            when (f) {
                HistoryFilter.BIKE -> ActivityMode.BIKE
                HistoryFilter.WALK -> ActivityMode.WALK
                else -> null
            },
        )
    }

    val uiState: StateFlow<HistoryUiState> = combine(
        filter,
        sessions,
        sessionRepository.observeTotals(),
        preferencesRepository.preferences,
    ) { f, list, totals, prefs ->
        HistoryUiState(
            loading = f == null,
            filter = f ?: HistoryFilter.ALL,
            sessions = list,
            totals = totals,
            unit = prefs.speedUnit,
        )
    }.stateIn(viewModelScope, SharingStarted.WhileSubscribed(5_000), HistoryUiState())

    fun setFilter(f: HistoryFilter) {
        filter.value = f
    }
}

data class SessionDetailUiState(
    val session: SessionEntity? = null,
    /** Route points grouped into segments (split at pauses and GPS gaps). */
    val segments: List<List<RoutePointEntity>> = emptyList(),
    val unit: SpeedUnit = SpeedUnit.KMH,
    val deleted: Boolean = false,
)

@HiltViewModel
class SessionDetailViewModel @Inject constructor(
    savedStateHandle: SavedStateHandle,
    private val sessionRepository: SessionRepository,
    preferencesRepository: UserPreferencesRepository,
) : ViewModel() {

    private val sessionId: Long = checkNotNull(savedStateHandle["sessionId"])
    private val segments = MutableStateFlow<List<List<RoutePointEntity>>>(emptyList())
    private val deleted = MutableStateFlow(false)

    init {
        viewModelScope.launch {
            segments.value = sessionRepository.getPoints(sessionId)
                .groupBy { it.segment }
                .toSortedMap()
                .values
                .filter { it.isNotEmpty() }
        }
    }

    val uiState: StateFlow<SessionDetailUiState> = combine(
        sessionRepository.observeSession(sessionId),
        segments,
        preferencesRepository.preferences,
        deleted,
    ) { session, segs, prefs, isDeleted ->
        SessionDetailUiState(session, segs, prefs.speedUnit, isDeleted)
    }.stateIn(viewModelScope, SharingStarted.WhileSubscribed(5_000), SessionDetailUiState())

    fun delete() = viewModelScope.launch {
        sessionRepository.deleteSession(sessionId)
        deleted.value = true
    }
}
