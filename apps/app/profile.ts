// SPDX-License-Identifier: AGPL-3.0-only
// The two minimal production profiles of the Account/Chat application. A build serves exactly one
// of them: its routes are chosen before bundling, so one profile's code never reaches the other's assets.
export const profiles = ["account", "chat"] as const;
export type Profile = (typeof profiles)[number];

export function isProfile(value: unknown): value is Profile {
  return typeof value === "string" && (profiles as readonly string[]).includes(value);
}

export function activeProfile(
  environment: Record<string, string | undefined> = process.env,
): Profile {
  const value = environment.ARCFORGES_PROFILE;
  if (!isProfile(value)) throw new Error(`Set ARCFORGES_PROFILE to one of: ${profiles.join(", ")}`);
  return value;
}
