# fs2ff (Flight Simulator to ForeFlight)

## What is it?
Note: astenlund is no longer supporting his branch. I'm going to attempt to do further work on this and continue the support. I have a day job and multiple hobbies don't expect much.

This fork of astenlund/fs2ff that implements the GDL90 protocol as an option instead of using XPlane protocol. This is a work in progress as there is still a lot of things left to do.
I originally started this work to understand GDL90 better and test changes I wanted in my Stratux device.

The executable is Windows .Net8 self-contained binary that has all of the needed files including the latest SimConnect.dll

## How do I use it?
1. Download the latest release. https://github.com/jeffdamp-wave/fs2ff/releases
1. Run the exe.
1. You can select auto-detect* if you are using ForeFlight. FF broadcasts a message and setting this will allow for auto-detection.
1. Run MSFS on the same computer(I'll add some remote options soon).
1. Click connect
1. For GDL90 Most EFBs will will detect the traffic and just start working. I recommend Stratux but you can also play around with Stratus emulation
1. If you select the X-Plane protocol this will require some addition steps in most EFBs. Example in GP you have to go to settings->Flight Simulation and turn it on.

## Does it work with other EFB apps?

Yes Any EFB that supports Stratux/Stratus GDL90 or X-Plane protocol over ethernet/wifi

### Garmin Pilot
Recent changes to GP have added better support for both Stratux and Stratus. I recommend using Stratux emulation on GP as you get a few more features (like turn coordinator)

### Other apps

- Levil Aviation App. From my testing this is the most responsive AHRS app I have found. Both GP and FF have a little lag in their synthetic vision implementations.
- FlyQ EFB (thanks, @erayymz)
- FltPlan GO 
- SkyDemon (tested some time ago)

## Does it work with other flight simulators?

This should work with any Flight Sim using SimConnect. I have not tested any myself. I would guess Prepare3D works?

## How do I build this?

1. Download and install [.NET Core SDK](https://dotnet.microsoft.com/download) and [Visual Studio Community](https://visualstudio.microsoft.com/downloads/).
1. Clone the repo or download and extract [a zip](https://github.com/jeffdamp-wave/fs2ff/archive/master.zip).
1. Install MSFS SDK (see instructions below).
1. Navigate to the SDK on your hard drive and find the following two files:
   - "MSFS SDK\SimConnect SDK\lib\SimConnect.dll"
   - "MSFS SDK\SimConnect SDK\lib\managed\Microsoft.FlightSimulator.SimConnect.dll"
1. Additional C lib files needed from vc_redistx64 (https://www.microsoft.com/en-us/download/details.aspx?id=48145):
   - concrt140.dll
   - msvcp140.dll
   - vcruntime140.dll
   - vcruntime140_1.dll
1. Create a folder called "lib" in the fs2ff folder (next to fs2ff.sln) and put the dll:s therein.
1. Open fs2ff.sln with Visual Studio.
1. Build by pressing Ctrl-Shift-B.
1. Or from command-line: `dotnet build`.
1. To build a self-contained executable, run: `dotnet publish -c Release -r win-x64 --self-contained fs2ff.sln`.

## Where do I get the MSFS SDK?

1. Hop into Flight Simulator.
1. Go to OPTIONS -> GENERAL -> DEVELOPERS and enable DEVELOPER MODE.
1. You will now have a new menu at the top. Click Help -> SDK Installer.
1. Let your browser download the installer and run it.
1. You might get a "Windows protected your PC" popup. If so, click More info -> Run anyway.
1. Go through the installation wizard and make sure that Core Components is selected.
1. When finished, you will likely find the SDK installed under "C:\MSFS SDK".

## What's with the "Windows protected your PC" popup?

This is Microsoft telling you that the app has not been cryptographically signed. If you worry about the binary you are welcome to build your own. I keep the binary in sync with the main branch.

## I have problems!

https://github.com/jeffdamp-wave/fs2ff/issues or https://github.com/jeffdamp-wave/fs2ff/discussions
