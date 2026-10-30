---
title: Sharing Configuration Across Devices
---

Preferis supports sharing configuration across devices through cloud storage such as OneDrive, Google Drive, and iCloud.

Select **General** in the sidebar, then click **Change Location** under **Configuration File** to change where the configuration file is stored. Choose **Move To…** to move the current file to a new location, such as a folder synced by your cloud storage service. On another device, choose **Use Existing File…** to select and use the synced file.

![](/img/docs/settings-configuration-file-01.png)

:::caution
Configuration sharing is intended for your own devices, rather than for a team. Sharing configuration with other people is not recommended, particularly because it can include commands to run.
:::

## Using different browsers on different operating systems or devices

Syncing the configuration file lets you share profiles and rules across devices. However, you may want a profile to open a different browser on each device. For example, you might use Microsoft Edge on Windows and Apple Safari on macOS.

Profiles offer two ways to customize this behavior: choosing a browser for each OS, or configuring a browser for each device.

### 1. Choosing a browser for each OS

To use different browsers on Windows and macOS, turn on **Different browser on each OS** in the profile's **Browser** section.

![](/img/docs/settings-share-devices-01.png)

This setting is shared across devices. If you need more specific settings, use the per-device option below.

### 2. Choosing a browser or action for each device

To use a different browser or action on each device, enable **Open with another browser or command on this computer** under **This Computer**.

The available options are similar to the profile's usual browser settings, but these settings override the shared configuration on the current device. They are stored separately from the synced configuration file (`config.json`).

![](/img/docs/settings-share-devices-02.png)

## Profile targets and empty profiles

Set a profile's **Opens with** option to **Set on each computer** to make it explicit that the profile needs per-device settings, without including detailed browser settings in the shared configuration.

This is the recommended approach when the profile is intended to use a different browser or action on each device from the outset.

![](/img/docs/settings-share-devices-04.png)

Profiles created with **Empty Profile…** also use **Set on each computer** by default.

![](/img/docs/settings-share-devices-03.png)
