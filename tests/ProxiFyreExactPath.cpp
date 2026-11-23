// Driver-free regression test for ProxiFyre v2.7.0's actual routing header.
#include <cstdint>
#include <cwctype>
#include <iostream>
#include <map>
#include <optional>
#include <string>
#include <vector>

// The routing header normally obtains this type from the Windows lookup layer.
namespace iphelper
{
    struct network_process
    {
        unsigned long id = 4;
        bool resolved = true;
        bool excluded = false;
        bool bypass_tcp = false;
        bool bypass_udp = false;
        std::optional<uint16_t> tcp_proxy_port, udp_proxy_port;
        std::wstring name, path_name;

        static std::wstring to_upper(std::wstring value)
        {
            for (auto& ch : value)
                ch = std::towupper(ch);
            return value;
        }
    };
}

#include "process_routing_policy.h"

int main()
{
    const std::wstring root = LR"(C:\Users\Test\AppData\Local\Discord\)";
    const std::wstring version = root + LR"(APP-1.0.9269\)";
    const std::vector<std::wstring> excludes;
    std::multimap<size_t, std::wstring> apps;
    for (const auto& path : {root + L"DISCORD.EXE", root + L"UPDATE.EXE",
                            version + L"DISCORD.EXE", version + L"UPDATER_LAUNCHER.EXE"})
        if (!proxy::associate_process_name_pattern(apps, 0, 1, path))
            return 2;

    const proxy::owner_match_rules rules{apps, excludes, true, 2};
    using transport = proxy::owner_transport;
    using family = proxy::owner_family;
    using action = proxy::proxy_port_action;
    const auto test = [&](const std::wstring& path, bool should_proxy, bool owner_resolved = true)
    {
        const auto check = [&]<transport T, family F>()
        {
            iphelper::network_process owner;
            owner.resolved = owner_resolved;
            owner.path_name = iphelper::network_process::to_upper(path);
            owner.name = owner.path_name.substr(owner.path_name.find_last_of(L'\\') + 1);
            const auto result = proxy::route_owner<T, F>(owner, [&]
            {
                return proxy::select_proxy_port(rules, owner,
                    [](size_t) { return proxy::proxy_route_target{true, true, 24739}; });
            });
            return should_proxy ? result.action == action::proxy && result.port == 24739
                                : result.action == action::none;
        };
        return check.template operator()<transport::tcp, family::ipv4>() &&
               check.template operator()<transport::udp, family::ipv4>() &&
               check.template operator()<transport::tcp, family::ipv6>() &&
               check.template operator()<transport::udp, family::ipv6>();
    };

    if (!test(root + L"Discord.exe", true) ||
        !test(root + L"UPDATE.EXE", true) ||
        !test(version + L"DISCORD.EXE", true) ||
        !test(version + L"UPDATER_LAUNCHER.EXE", true) ||
        !test(version + L"DISCORD.EXE.BACKUP.EXE", false) ||
        !test(root + L"UPDATE.EXE2.EXE", false) ||
        !test(LR"(C:\Other\DISCORD.EXE)", false) ||
        !test(version + L"DISCORD.EXE", false, false))
    {
        std::cerr << "Discord exact-path selection regression failed\n";
        return 1;
    }

    std::cout << "Exact-path selection passed for TCP/UDP and IPv4/IPv6 routing decisions\n";
}
