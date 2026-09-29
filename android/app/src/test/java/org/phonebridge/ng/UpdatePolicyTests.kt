package org.phonebridge.ng

import org.junit.Assert.assertEquals
import org.junit.Assert.assertThrows
import org.junit.Test

class UpdatePolicyTests {
    @Test fun newerFormalReleaseIsAvailable() {
        val result = release(current = "0.2.4", latest = "0.2.5")
        assertEquals(UpdateAvailability.AVAILABLE, result.availability)
        assertEquals("0.2.5", result.asset.version)
    }

    @Test fun olderFormalReleaseNeverDowngradesCandidate() {
        assertEquals(UpdateAvailability.CURRENT, release(current = "0.2.4", latest = "0.2.3").availability)
    }

    @Test fun previewSuffixUsesBaseVersion() {
        assertEquals(UpdateAvailability.CURRENT,
            release(current = "0.2.4-ui-preview-r1", latest = "0.2.4").availability)
    }

    @Test fun untrustedUrlAndMissingDigestAreRejected() {
        val url = assertThrows(UpdateFailure::class.java) {
            release(current = "0.2.4", latest = "0.2.5", host = "example.com")
        }
        assertEquals("update_response_invalid", url.code)
        val digest = assertThrows(UpdateFailure::class.java) {
            release(current = "0.2.4", latest = "0.2.5", digest = "")
        }
        assertEquals("update_response_invalid", digest.code)
    }

    private fun release(current: String, latest: String, host: String = "github.com",
                        digest: String = "sha256:" + "A".repeat(64)): UpdateCheck {
        val name = "PhoneBridge-NG-$latest.apk"
        return UpdatePolicy.evaluate(current, "v$latest", false, false, name, "uploaded", 1024, digest,
            "https://$host/yang137197/PhoneBridge-NG/releases/download/v$latest/$name")
    }
}
