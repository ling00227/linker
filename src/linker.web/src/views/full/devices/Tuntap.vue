<template>
    <el-table-column prop="tuntap" :label="tuntap.show?'虚拟网卡':''" width="160">
        <template #header>
           <a v-if="tuntap.show" href="javascript:;" class="a-line" @click="handleShowLease">虚拟网卡</a>
        </template>
        <template #default="scope">
            <div v-if="tuntap.show && tuntap.list[scope.row.MachineId]">
                <TuntapShow :config="true" :item="scope.row" @edit="handleTuntapIP" @refresh="handleTuntapRefresh"></TuntapShow>
            </div> 
        </template>
    </el-table-column>
</template>
<script>
import { useTuntap } from './tuntap';
import TuntapShow from './TuntapShow.vue';
export default {
    emits: ['edit','refresh'],
    components:{TuntapShow},
    setup(props, { emit }) {

        const tuntap = useTuntap();

        const handleTuntapIP = (_tuntap) => {
            emit('edit',_tuntap);
        }
        const handleTuntapRefresh = ()=>{
            emit('refresh');
        }
        const handleShowLease = ()=>{
            tuntap.value.showLease = true;
        }
       
        return {
            tuntap, handleTuntapIP,handleTuntapRefresh,handleShowLease
        }
    }
}
</script>
<style lang="stylus" scoped>
</style>