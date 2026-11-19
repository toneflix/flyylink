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
    body: "Reply from your keyboard. Your computer's show on your phone.",
  },
  {
    icon: MessageSquare,
    title: "Texts",
    body: "Read and reply.",
  },
  {
    icon: Phone,
    title: "Calls",
    body: "Answer on your computer.",
  },
  {
    icon: FolderUp,
    title: "Files",
    body: "Any size, from any app.",
  },
  {
    icon: Radar,
    title: "FlyyDrop",
    body: "Send to anyone nearby.",
  },
  {
    icon: MonitorSmartphone,
    title: "Screen mirroring",
    body: "Use your phone from your computer.",
  },
  {
    icon: Images,
    title: "Photos",
    body: "Your latest shots, on your computer.",
  },
  {
    icon: FolderOpen,
    title: "Phone storage",
    body: "Every folder, in Finder or File Explorer.",
  },
  {
    icon: Clipboard,
    title: "Clipboard",
    body: "Copy on one, paste on the other.",
  },
  {
    icon: MapPin,
    title: "Find your phone",
    body: "Ring it, even on silent.",
  },
  {
    icon: Music,
    title: "Music, battery and signal",
    body: "Play, pause, skip, and check the battery and signal.",
  },
  {
    icon: MousePointer2,
    title: "Computer remote",
    body: "Your phone is a touchpad, keyboard and slide clicker.",
  },
  {
    icon: Tv,
    title: "TV remote",
    body: "Your phone or computer is the remote. Type and point, too.",
  },
  {
    icon: Cast,
    title: "Play on TV",
    body: "Photos, videos and music on your smart TV.",
  },
  {
    icon: Smartphone,
    title: "Several phones",
    body: "Pair as many as you like.",
  },
  {
    icon: Laptop,
    title: "Computer to computer",
    body: "Share files and the clipboard between Macs and PCs.",
  },
  {
    icon: Wifi,
    title: "No Wi-Fi? Still works.",
    body: "Connects through your phone's hotspot.",
  },
];

export const platforms = [
  { name: "Android", href: site.playStore, needs: "Android 8.0 or later", detail: "Free on phones, tablets and TVs", playStore: true },
  { name: "Amazon Fire", href: site.amazonAppstore, needs: "Fire OS 7 or later", detail: "Free on Fire TV and tablets" },
  { name: "Mac", href: "/download/mac", needs: "macOS 13 Ventura or later", detail: "Try it free, then pick a plan" },
  { name: "Windows", href: "/download/windows", needs: "Windows 10 (1809) or later", detail: "Try it free, then pick a plan" },
];

export const steps = [
  { title: "Install", body: "Phone, computer or TV." },
  { title: "Same Wi-Fi", body: "Or join your phone's hotspot." },
  { title: "Pair once", body: "Check the code matches." },
];
