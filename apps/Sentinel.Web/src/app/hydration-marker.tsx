"use client";

import { useEffect } from "react";

// Marks the document once React has hydrated it, so a test that types into a form never races the hydration that wires
// the form's server action. Renders nothing.
export function HydrationMarker() {
  useEffect(() => {
    document.documentElement.dataset.hydrated = "true";
  }, []);

  return null;
}
