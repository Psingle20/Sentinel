"use client";

import { useEffect } from "react";

import { type AnalyticsEvents, track } from "./track";

// Records one view of a page, once per mount.
export function TrackView<Name extends keyof AnalyticsEvents>({ name, properties }: { name: Name; properties: AnalyticsEvents[Name] }) {
  const key = JSON.stringify(properties);

  useEffect(() => {
    track(name, JSON.parse(key) as AnalyticsEvents[Name]);
  }, [name, key]);

  return null;
}
