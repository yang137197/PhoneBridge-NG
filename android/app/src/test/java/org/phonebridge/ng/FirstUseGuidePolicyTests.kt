package org.phonebridge.ng

import org.junit.Assert.assertEquals
import org.junit.Test

class FirstUseGuidePolicyTests {
    @Test fun freshInstallShowsGuide() {
        assertEquals(FirstUseGuideDecision.SHOW, FirstUseGuidePolicy.decide(completed = false, hasPairedComputers = false))
    }

    @Test fun existingPairingMigratesWithoutGuide() {
        assertEquals(FirstUseGuideDecision.SKIP_AND_MARK_COMPLETED,
            FirstUseGuidePolicy.decide(completed = false, hasPairedComputers = true))
    }

    @Test fun completedGuideNeverShowsAgain() {
        assertEquals(FirstUseGuideDecision.SKIP, FirstUseGuidePolicy.decide(completed = true, hasPairedComputers = false))
        assertEquals(FirstUseGuideDecision.SKIP, FirstUseGuidePolicy.decide(completed = true, hasPairedComputers = true))
    }
}
