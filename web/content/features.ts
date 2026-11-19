import type { LucideIcon } from "lucide-react";
import { site } from "./site";
import {
  Bell,
  MapPin,
  Cast,
  Clipboard,
  FolderOpen,
  FolderUp,
  Images,
  Laptop,
  MessageSquare,
  MonitorSmartphone,
  MousePointer2,
  Music,
  Phone,
  Radar,
  Smartphone,
  Tv,
  Wifi,
} from "lucide-react";

export type Feature = {
  icon: LucideIcon;
  title: string;
  body: string;
};

export const features: Feature[] = [
  {
    icon: Bell,
    title: "Notifications",
    body: "See your phone's notifications on your computer and reply right from your keyboard. Your computer's show up on your phone too.",
  },
  {
    icon: MessageSquare,
    title: "Texts",
    body: "Read your texts and reply to them from your computer.",
  },
  {
    icon: Phone,
    title: "Calls",
    body: "See who's calling and answer without reaching for your phone.",
  },
  {
    icon: FolderUp,
    title: "Files",
    body: "Send files of any size both ways, straight from any app.",
  },
  {
    icon: Radar,
    title: "FlyyDrop",
    body: "Share with people nearby, even if you've never paired with them.",
  },
  {
    icon: MonitorSmartphone,
    title: "Screen mirroring",
    body: "See your phone's screen on your computer and use it with your mouse and keyboard.",
  },
  {
    icon: Images,
    title: "Photos",
    body: "See the latest photos from your phone on your computer.",
  },
  {
    icon: FolderOpen,
    title: "Phone storage",
    body: "Browse your phone's folders in Finder or File Explorer, like any other drive.",
  },
  {
    icon: Clipboard,
    title: "Clipboard",
    body: "Copy something on your phone and paste it on your computer, or the other way round.",
  },
  {
    icon: MapPin,
    title: "Find your phone",
    body: "Lost it in the couch again? Make it ring, even when it's on silent.",
  },
  {
    icon: Music,
    title: "Music, battery and signal",
    body: "Control what's playing on your phone and keep an eye on its battery and signal.",
  },
  {
    icon: MousePointer2,
    title: "Computer remote",
    body: "Use your phone as a touchpad, a keyboard or a clicker for your slides.",
  },
  {
    icon: Tv,
    title: "TV remote",
    body: "Control your TV from your phone or computer, and type with a real keyboard.",
  },
  {
    icon: Cast,
    title: "Play on TV",
    body: "Show your photos, videos and music on your smart TV.",
  },
  {
    icon: Smartphone,
    title: "Several phones",
    body: "Connect as many phones as you like and switch between them.",
  },
  {
    icon: Laptop,
    title: "Computer to computer",
    body: "Send files and share your clipboard between your Macs and PCs.",
  },
  {
    icon: Wifi,
    title: "No Wi-Fi? No problem.",
    body: "FlyyLink works over your phone's hotspot too.",
  },
];

export const platforms = [
  { name: "Android", href: site.playStore, needs: "Android 8.0 or later", detail: "Free on phones, tablets and TVs", playStore: true },
  { name: "Amazon Fire", href: site.amazonAppstore, needs: "Fire OS 7 or later", detail: "Free on Fire TV and tablets" },
  { name: "Mac", href: "/download/mac", needs: "macOS 13 Ventura or later", detail: "Try it free, then pick a plan" },
  { name: "Windows", href: "/download/windows", needs: "Windows 10 (1809) or later", detail: "Try it free, then pick a plan" },
];

export const steps = [
  { title: "Install FlyyLink", body: "Get it on your phone and your computer or TV." },
  { title: "Join the same Wi-Fi", body: "Or connect your computer to your phone's hotspot." },
  { title: "Pair them", body: "Check the code matches on both screens, and you're done." },
];
