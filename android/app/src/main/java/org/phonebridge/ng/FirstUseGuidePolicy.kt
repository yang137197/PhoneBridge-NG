package org.phonebridge.ng

internal const val FIRST_USE_GUIDE_COMPLETED = "first_use_guide_v1_completed"

internal enum class FirstUseGuideDecision { SHOW, SKIP, SKIP_AND_MARK_COMPLETED }

internal object FirstUseGuidePolicy {
    fun decide(completed: Boolean, hasPairedComputers: Boolean): FirstUseGuideDecision = when {
        completed -> FirstUseGuideDecision.SKIP
        hasPairedComputers -> FirstUseGuideDecision.SKIP_AND_MARK_COMPLETED
        else -> FirstUseGuideDecision.SHOW
    }
}
