// SPDX-License-Identifier: AGPL-3.0-only
import { useLoaderData } from "react-router";
import { loadHomePage } from "../site-content.server";
import { metadataForPage } from "../site-metadata";
import { HomePageView } from "../site-pages";

export function loader() {
  return loadHomePage();
}

export function meta({ loaderData }: { loaderData: ReturnType<typeof loadHomePage> | undefined }) {
  return loaderData ? metadataForPage(loaderData) : [];
}

export default function Home() {
  const page = useLoaderData<typeof loader>();
  return <HomePageView page={page} />;
}
